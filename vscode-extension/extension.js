// DevAgents Local — VS Code extension (plain JavaScript, no build step).
const vscode = require('vscode');

const out = vscode.window.createOutputChannel('DevAgents');
const history = [];
let lastResult = null;

const STAGES = {
  translate_in: 'Türkçe → İngilizce', context: 'Proje bağlamı', analyze: 'Analiz', code: 'Kodlama',
  test: 'Test', review: 'Review', revise: 'Kodlama (revizyon)', translate_out: 'İngilizce → Türkçe'
};

const cfg = () => vscode.workspace.getConfiguration('devagents');
const serverUrl = () => cfg().get('serverUrl').replace(/\/$/, '');

function workspaceFolder() {
  const f = vscode.workspace.workspaceFolders?.[0];
  if (!f) throw new Error('Önce bir klasör (workspace) açın.');
  return f;
}

/** Collects project files for "inline" mode. The server ranks them and keeps what fits the model context. */
async function gatherFiles() {
  const c = cfg();
  const maxFiles = c.get('maxFiles'), maxBytes = c.get('maxTotalKB') * 1024;
  const include = '**/*.{cs,csproj,sln,props,targets,json,md,cshtml,razor,sql,yml,yaml,xml,config,ts,tsx,js,jsx,py,java,go,proto}';
  const exclude = '**/{bin,obj,node_modules,.git,.vs,.idea,.vscode,packages,dist,out,TestResults,coverage,.venv,venv}/**';
  const uris = await vscode.workspace.findFiles(include, exclude, maxFiles);
  const dec = new TextDecoder();
  const files = [];
  let total = 0;
  for (let i = 0; i < uris.length && total < maxBytes; i += 25) {
    const batch = await Promise.all(uris.slice(i, i + 25).map(async uri => {
      try {
        const data = await vscode.workspace.fs.readFile(uri);
        if (data.byteLength > 200 * 1024) return null;
        return { path: vscode.workspace.asRelativePath(uri, false), content: dec.decode(data), pinned: false };
      } catch { return null; }
    }));
    for (const f of batch) {
      if (f && total + f.content.length <= maxBytes) { files.push(f); total += f.content.length; }
    }
  }
  return files;
}

async function run(question, pinned) {
  const folder = workspaceFolder();
  const c = cfg();
  const body = {
    question,
    history,
    files: [],
    options: { maxReviewIterations: c.get('maxReviewIterations') }
  };

  if (c.get('contextMode') === 'path') {
    body.projectPath = c.get('serverProjectPath') || folder.uri.fsPath;
  } else {
    body.files = await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Window, title: 'DevAgents: proje dosyaları toplanıyor…' }, gatherFiles);
  }
  if (pinned) body.files.push(...pinned);

  out.clear();
  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: 'DevAgents', cancellable: true },
    async (progress, token) => {
      const ac = new AbortController();
      token.onCancellationRequested(() => ac.abort());
      progress.report({ message: 'başlatılıyor…' });

      const res = await fetch(serverUrl() + '/api/chat/stream', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body), signal: ac.signal
      });
      if (!res.ok) throw new Error(`API ${res.status}: ${await res.text()}`);

      const dec = new TextDecoder();
      let buf = '', stage = '';
      for await (const chunk of res.body) {
        buf += dec.decode(chunk, { stream: true });
        let nl;
        while ((nl = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
          if (!line) continue;
          const ev = JSON.parse(line);
          if (ev.type === 'stage_start') {
            stage = STAGES[ev.stage] || ev.stage;
            progress.report({ message: `${stage}${ev.text ? ' · ' + ev.text : ''}` });
            out.appendLine(`\n=== ${stage} (${ev.text || ''}) ===`);
          } else if (ev.type === 'token') {
            out.append(ev.text);
          } else if (ev.type === 'error') {
            throw new Error(ev.text);
          } else if (ev.type === 'final') {
            lastResult = ev.data;
            history.push({ request: ev.data.englishRequest, code: ev.data.code });
            if (history.length > 6) history.shift();
          }
        }
      }
    });

  if (lastResult) await showResult(lastResult);
}

async function showResult(r) {
  const doc = await vscode.workspace.openTextDocument({ content: r.finalMarkdown, language: 'markdown' });
  await vscode.window.showTextDocument(doc, { viewColumn: vscode.ViewColumn.Beside, preview: false });
  vscode.commands.executeCommand('markdown.showPreview').then(undefined, () => { });

  const verdict = r.approved ? 'Review onayladı' : 'Review değişiklik istedi';
  if (r.files.length) {
    const pick = await vscode.window.showInformationMessage(
      `${verdict}. ${r.files.length} dosya üretildi (${Math.round(r.seconds)} sn).`, 'Dosyaları uygula');
    if (pick) await applyLast();
  } else {
    vscode.window.showInformationMessage(`${verdict}. (Ayrıştırılabilir dosya üretilmedi)`);
  }
}

async function applyLast() {
  if (!lastResult?.files?.length) return vscode.window.showWarningMessage('Uygulanacak dosya yok. Önce bir soru sorun.');
  const folder = workspaceFolder();
  const items = lastResult.files.map(f => ({ label: f.path, description: f.kind === 'test' ? 'test' : 'kod', picked: true, file: f }));
  const chosen = await vscode.window.showQuickPick(items, { canPickMany: true, title: 'Projeye yazılacak dosyaları seçin' });
  if (!chosen?.length) return;

  const enc = new TextEncoder();
  let written = 0;
  for (const { file } of chosen) {
    if (file.path.startsWith('/') || file.path.includes('..')) continue; // never write outside the workspace
    const target = vscode.Uri.joinPath(folder.uri, file.path);
    let exists = true;
    try { await vscode.workspace.fs.stat(target); } catch { exists = false; }

    if (exists) {
      const choice = await vscode.window.showWarningMessage(
        `${file.path} zaten var.`, { modal: true }, 'Üzerine yaz', 'Farkı göster');
      if (choice === 'Farkı göster') {
        const right = await vscode.workspace.openTextDocument({ content: file.content, language: 'csharp' });
        await vscode.commands.executeCommand('vscode.diff', target, right.uri, `${file.path} ⇄ DevAgents`);
        continue;
      }
      if (choice !== 'Üzerine yaz') continue;
    }
    await vscode.workspace.fs.createDirectory(vscode.Uri.joinPath(target, '..'));
    await vscode.workspace.fs.writeFile(target, enc.encode(file.content));
    written++;
  }
  vscode.window.showInformationMessage(`${written} dosya yazıldı.`);
}

async function guard(fn) {
  try { await fn(); }
  catch (e) {
    if (e.name === 'AbortError') return vscode.window.showInformationMessage('DevAgents: iptal edildi.');
    out.appendLine('\nHATA: ' + e.message); out.show(true);
    vscode.window.showErrorMessage('DevAgents: ' + e.message);
  }
}

function activate(context) {
  context.subscriptions.push(
    vscode.commands.registerCommand('devagents.ask', () => guard(async () => {
      const q = await vscode.window.showInputBox({ title: 'DevAgents', prompt: 'Ne geliştirelim? (Türkçe yazabilirsiniz)', ignoreFocusOut: true });
      if (!q) return;
      const ed = vscode.window.activeTextEditor;
      const pinned = ed && ed.document.uri.scheme === 'file' && ed.document.getText().length < 60000
        ? [{ path: vscode.workspace.asRelativePath(ed.document.uri, false), content: ed.document.getText(), pinned: true }]
        : [];
      await run(q, pinned);
    })),

    vscode.commands.registerCommand('devagents.askSelection', () => guard(async () => {
      const ed = vscode.window.activeTextEditor;
      if (!ed || ed.selection.isEmpty) return vscode.window.showWarningMessage('Önce kod seçin.');
      const q = await vscode.window.showInputBox({ title: 'DevAgents — seçili kod', prompt: 'Seçili kodla ne yapılsın?', ignoreFocusOut: true });
      if (!q) return;
      const rel = vscode.workspace.asRelativePath(ed.document.uri, false);
      const selected = ed.document.getText(ed.selection);
      await run(`${q}\n\n(Dosya: ${rel}, seçili kod aşağıdadır)`, [
        { path: rel, content: ed.document.getText().slice(0, 60000), pinned: true },
        { path: `${rel}.selection.txt`, content: selected, pinned: true }
      ]);
    })),

    vscode.commands.registerCommand('devagents.applyLast', () => guard(applyLast)),
    vscode.commands.registerCommand('devagents.resetHistory', () => { history.length = 0; vscode.window.showInformationMessage('DevAgents: geçmiş sıfırlandı.'); }),
    vscode.commands.registerCommand('devagents.openChat', () => vscode.env.openExternal(vscode.Uri.parse(serverUrl())))
  );
}

function deactivate() { }
module.exports = { activate, deactivate };
