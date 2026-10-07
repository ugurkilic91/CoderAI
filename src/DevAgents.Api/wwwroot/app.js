(() => {
  const $ = (s, root = document) => root.querySelector(s);
  const make = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; };

  const AGENTS = [
    ['Router', 'Router (ajan seçimi)'],
    ['Translator', 'Çeviri (TR → EN)'],
    ['Analyzer', 'Analiz Agent'],
    ['Coder', 'Kodlama Agent'],
    ['Tester', 'Test Agent'],
    ['Reviewer', 'Review Agent'],
    ['BackTranslator', 'Çeviri (EN → TR)']
  ];
  const STAGES = {
    translate_in: 'Türkçe → İngilizce',
    route: 'Router · ajan seçimi',
    answer: 'Cevap Agent',
    context: 'Proje bağlamı',
    analyze: 'Analiz Agent',
    code: 'Kodlama Agent',
    test: 'Test Agent',
    review: 'Review Agent',
    revise: 'Kodlama Agent · revizyon',
    translate_out: 'İngilizce → Türkçe'
  };

  const state = { history: [], files: [], busy: false, abort: null, lastResult: null };
  const el = {
    messages: $('#messages'), empty: $('#empty'), input: $('#input'), send: $('#sendBtn'),
    project: $('#projectPath'), extra: $('#extra'), iters: $('#iters'), trOut: $('#trOut'),
    fileInput: $('#fileInput'), fileList: $('#fileList'), status: $('#status'), side: $('#side')
  };

  /* ---------------- markdown (small, dependency-free) ---------------- */
  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const inline = s => esc(s)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

  function codeBlock(code, lang) {
    return `<div class="code"><div class="code-head"><span>${esc(lang || 'text')}</span>` +
      `<button type="button" data-copy>Kopyala</button></div><pre><code>${esc(code)}</code></pre></div>`;
  }

  function renderMarkdown(src) {
    const lines = src.replace(/\r/g, '').split('\n');
    const out = [];
    let i = 0, list = null, para = [];
    const flushPara = () => { if (para.length) { out.push('<p>' + inline(para.join(' ')) + '</p>'); para = []; } };
    const flushList = () => { if (list) { out.push(`</${list}>`); list = null; } };

    while (i < lines.length) {
      const line = lines[i];
      const fence = line.match(/^```(\w*)/);
      if (fence) {
        flushPara(); flushList();
        const buf = []; i++;
        while (i < lines.length && !/^```\s*$/.test(lines[i])) buf.push(lines[i++]);
        i++;
        out.push(codeBlock(buf.join('\n'), fence[1]));
        continue;
      }
      const h = line.match(/^(#{1,5})\s+(.*)/);
      if (h) {
        flushPara(); flushList();
        const file = h[2].match(/^FILE:\s*(.+)$/i);
        if (file) out.push(`<div class="filehead">${esc(file[1].trim())}</div>`);
        else { const n = Math.min(h[1].length, 5); out.push(`<h${n}>${inline(h[2])}</h${n}>`); }
        i++; continue;
      }
      const ul = line.match(/^\s*[-*]\s+(.*)/), ol = line.match(/^\s*\d+[.)]\s+(.*)/);
      if (ul || ol) {
        flushPara();
        const tag = ul ? 'ul' : 'ol';
        if (list !== tag) { flushList(); out.push(`<${tag}>`); list = tag; }
        out.push('<li>' + inline((ul || ol)[1]) + '</li>');
        i++; continue;
      }
      if (!line.trim()) { flushPara(); flushList(); i++; continue; }
      flushList();
      para.push(line.trim());
      i++;
    }
    flushPara(); flushList();
    return out.join('\n');
  }

  /* ---------------- sidebar ---------------- */
  async function loadSidebar() {
    try {
      const [cfg, models] = await Promise.all([
        fetch('/api/config').then(r => r.json()),
        fetch('/api/models').then(r => r.ok ? r.json() : [])
      ]);
      setStatus(true, `Ollama bağlı · ${models.length} model`);
      const box = $('#modelSelects');
      box.innerHTML = '';
      for (const [key, label] of AGENTS) {
        const resolved = (cfg.agents || []).find(a => a.name === key);
        const wrap = make('div', 'sel');
        wrap.append(make('label', null, label));
        const sel = make('select');
        sel.dataset.agent = key;
        sel.append(new Option(`(config) ${resolved ? resolved.model : '?'}`, ''));
        models.forEach(m => sel.append(new Option(m, m)));
        wrap.append(sel);
        box.append(wrap);
      }
    } catch (e) { setStatus(false, 'Ollama’ya ulaşılamıyor'); }

    try {
      const p = await fetch('/api/projects').then(r => r.json());
      const dl = $('#projectList');
      (p.projects || []).forEach(x => dl.append(new Option(x.path, x.name)));
      if ((p.roots || []).length && !el.project.value) el.project.placeholder = 'Klasör adı, örn: ' + ((p.projects[0] || {}).name || 'MyApp');
    } catch { /* optional */ }
  }
  function setStatus(ok, text) {
    el.status.className = 'status ' + (ok ? 'ok' : 'bad');
    el.status.querySelector('span').textContent = text;
  }

  $('#fileBtn').onclick = () => el.fileInput.click();
  el.fileInput.onchange = async () => {
    for (const f of el.fileInput.files) {
      if (f.size > 200_000) { alert(`${f.name} çok büyük (en fazla 200 KB).`); continue; }
      state.files.push({ name: f.name, content: await f.text() });
    }
    el.fileInput.value = '';
    renderFiles();
  };
  function renderFiles() {
    el.fileList.innerHTML = '';
    state.files.forEach((f, i) => {
      const li = make('li');
      li.append(make('span', null, esc(f.name)));
      const rm = make('button', null, '✕'); rm.type = 'button'; rm.title = 'Kaldır';
      rm.onclick = () => { state.files.splice(i, 1); renderFiles(); };
      li.append(rm);
      el.fileList.append(li);
    });
  }

  $('#previewBtn').onclick = async () => {
    const body = { projectPath: el.project.value.trim() || null, question: el.input.value, files: pinnedFiles() };
    const dlg = $('#ctxDialog'), box = $('#ctxBody');
    box.innerHTML = '<p>Yükleniyor…</p>'; dlg.showModal();
    const r = await fetch('/api/context/preview', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) { const e = await r.json().catch(() => ({})); box.innerHTML = `<p class="err">${esc(e.detail || e.title || 'Hata')}</p>`; return; }
    const d = await r.json();
    box.innerHTML = `<p>${d.totalFiles} dosya indekslendi · ${d.included.length} dosya ajanlara gidiyor · ${d.chars.toLocaleString('tr')} karakter</p>` +
      (d.warning ? `<p class="err">${esc(d.warning)}</p>` : '') +
      `<h4>Seçilen dosyalar</h4><ul>${d.included.map(f => `<li><code>${esc(f)}</code></li>`).join('') || '<li>—</li>'}</ul>` +
      `<h4>Genel bakış</h4><pre>${esc(d.overview)}</pre>`;
  };

  $('#newChat').onclick = () => {
    state.history = []; state.lastResult = null;
    el.messages.querySelectorAll('.msg').forEach(m => m.remove());
    el.empty.style.display = '';
  };
  $('#menuBtn').onclick = () => el.side.classList.toggle('open');
  document.querySelectorAll('.sample').forEach(b => b.onclick = () => { el.input.value = b.textContent; el.input.focus(); });

  /* ---------------- chat ---------------- */
  const pinnedFiles = () => state.files.map(f => ({ path: f.name, content: f.content, pinned: true }));

  el.input.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); onSend(); }
  });
  el.input.addEventListener('input', () => { el.input.style.height = 'auto'; el.input.style.height = Math.min(el.input.scrollHeight, 200) + 'px'; });
  el.send.onclick = () => state.busy ? state.abort?.abort() : onSend();
  el.messages.addEventListener('click', e => {
    const b = e.target.closest('[data-copy]');
    if (!b) return;
    navigator.clipboard.writeText(b.closest('.code').querySelector('code').textContent);
    b.textContent = 'Kopyalandı'; setTimeout(() => b.textContent = 'Kopyala', 1200);
  });

  function addUser(text) {
    el.empty.style.display = 'none';
    const m = make('div', 'msg user'); m.append(make('div', 'bubble', esc(text)));
    el.messages.append(m); scroll();
  }
  function scroll() { el.messages.scrollTop = el.messages.scrollHeight; }
  function setBusy(b) {
    state.busy = b;
    el.send.textContent = b ? 'Durdur' : 'Gönder';
    el.send.classList.toggle('stop', b);
  }

  async function onSend() {
    const question = el.input.value.trim();
    if (!question || state.busy) return;
    el.input.value = ''; el.input.style.height = 'auto';
    addUser(question);

    const overrides = {};
    document.querySelectorAll('#modelSelects select').forEach(s => { if (s.value) overrides[s.dataset.agent] = s.value; });

    const body = {
      question,
      projectPath: el.project.value.trim() || null,
      extraContext: el.extra.value.trim() || null,
      files: pinnedFiles(),
      history: state.history,
      options: { mode: $('#mode').value, maxReviewIterations: +el.iters.value, translateOutput: el.trOut.checked, modelOverrides: overrides }
    };

    const msg = make('div', 'msg bot');
    const card = make('div', 'card');
    const rail = make('ol', 'rail');
    const answer = make('div', 'answer'); answer.hidden = true;
    card.append(rail, answer); msg.append(card); el.messages.append(msg); scroll();

    const ui = { rail, answer, card, current: null };
    state.abort = new AbortController();
    setBusy(true);
    try {
      const res = await fetch('/api/chat/stream', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body), signal: state.abort.signal
      });
      if (!res.ok) throw new Error(await res.text());
      const reader = res.body.getReader(), dec = new TextDecoder();
      let buf = '';
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        buf += dec.decode(value, { stream: true });
        let nl;
        while ((nl = buf.indexOf('\n')) >= 0) {
          const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
          if (line) onEvent(JSON.parse(line), ui);
        }
      }
    } catch (e) {
      if (ui.current) ui.current.li.className = 'fail';
      const text = e.name === 'AbortError' ? 'İşlem durduruldu.' : e.message;
      card.append(make('div', 'err', esc(text)));
    } finally { setBusy(false); scroll(); }
  }

  function onEvent(ev, ui) {
    switch (ev.type) {
      case 'stage_start': {
        const li = make('li', 'run');
        const d = make('details'); d.open = true;
        const sum = make('summary');
        sum.append(make('span', 'name', STAGES[ev.stage] || ev.stage));
        const meta = make('span', 'meta', ev.text ? esc(ev.text) : '');
        sum.append(meta);
        const pre = make('pre', 'live');
        d.append(sum, pre); li.append(d); ui.rail.append(li);
        // collapse previous finished stages to keep the rail compact
        ui.rail.querySelectorAll('li.done details').forEach(x => x.open = false);
        ui.current = { li, meta, pre, name: STAGES[ev.stage] || ev.stage, model: ev.text };
        break;
      }
      case 'token':
        if (ui.current) { ui.current.pre.textContent += ev.text; ui.current.pre.scrollTop = ui.current.pre.scrollHeight; }
        break;
      case 'stage_end':
        if (ui.current) {
          ui.current.li.className = 'done';
          const extra = ev.stage === 'context' && ev.data
            ? ` · ${ev.data.includedFiles.length}/${ev.data.totalFiles} dosya`
            : '';
          ui.current.meta.textContent = `${ui.current.model || ''}${extra} · ${ev.seconds.toFixed(1)} sn`.replace(/^ · /, '');
          if (ev.stage === 'context' && ev.data) {
            ui.current.pre.textContent = ev.data.includedFiles.join('\n') || '(dosya seçilmedi)';
            if (ev.text) ui.current.pre.textContent += '\n\n⚠ ' + ev.text;
          }
          if (!ui.current.pre.textContent.trim()) ui.current.pre.remove();
          ui.current.li.querySelector('details').open = false;
        }
        break;
      case 'plan': {
        const p = ev.data, on = [['answer', 'cevap'], ['analyze', 'analiz'], ['code', 'kod'], ['test', 'test'], ['review', 'review']]
          .filter(([k]) => p[k]).map(([, n]) => n).join(' → ');
        const note = make('div', 'plan', `<strong>Plan:</strong> ${esc(on)}${p.reason ? ' — ' + esc(p.reason) : ''}`);
        ui.card.insertBefore(note, ui.rail);
        break;
      }
      case 'final': showFinal(ev.data, ui); break;
      case 'error':
        if (ui.current) ui.current.li.className = 'fail';
        ui.card.append(make('div', 'err', esc(ev.text)));
        break;
    }
    scroll();
  }

  function showFinal(r, ui) {
    state.lastResult = r;
    state.history.push({ request: r.englishRequest, code: r.code });
    if (state.history.length > 6) state.history.shift();

    ui.answer.hidden = false;
    ui.answer.innerHTML = renderMarkdown(r.finalMarkdown);

    const foot = make('div', 'footnote');
    if (r.review) foot.append(make('span', 'verdict ' + (r.approved ? 'ok' : 'warn'), r.approved ? 'Review onayladı' : 'Review değişiklik istedi'));
    foot.append(make('span', null, `${r.seconds.toFixed(0)} sn · ${r.files.length} dosya`));
    if (r.files.length) {
      const dl = make('button', null, 'Dosyaları indir (.json)'); dl.type = 'button';
      dl.onclick = () => download('devagents-files.json', JSON.stringify(r.files, null, 2));
      const all = make('button', null, 'Tüm kodu kopyala'); all.type = 'button';
      all.onclick = () => navigator.clipboard.writeText(r.files.map(f => `// ${f.path}\n${f.content}`).join('\n\n'));
      foot.append(dl, all);
    }
    ui.answer.append(foot);
  }

  function download(name, text) {
    const a = make('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    a.download = name; a.click(); URL.revokeObjectURL(a.href);
  }

  loadSidebar();
})();
