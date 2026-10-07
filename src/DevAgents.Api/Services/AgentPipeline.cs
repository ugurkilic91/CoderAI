using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevAgents.Models;
using DevAgents.Options;
using Microsoft.Extensions.Options;

namespace DevAgents.Services;

/// <summary>
/// Turkish question → English → Router (decides which agents are needed) →
/// [Answer | Analyzer → Coder → (Tester → Reviewer → Coder revision loop)] → Turkish result.
/// Every agent has its own model/temperature/context size, configured in appsettings / config/local.json.
/// </summary>
public sealed class AgentPipeline(
    OllamaClient ollama,
    ProjectContextBuilder contextBuilder,
    PromptStore prompts,
    IOptionsMonitor<PipelineSettings> pipelineSettings)
{
    private static AgentPlan Full(string why) => new("feature", false, true, true, true, true, true, why);

    public async Task<PipelineResult> RunAsync(ChatRequest req, Func<PipelineEvent, Task> emit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            throw new ArgumentException("Soru boş olamaz.");

        var cfg = pipelineSettings.CurrentValue;
        var overrides = req.Options?.ModelOverrides;
        var maxIter = Math.Clamp(req.Options?.MaxReviewIterations ?? cfg.MaxReviewIterations, 0, 5);
        var translateOut = req.Options?.TranslateOutput ?? cfg.TranslateOutput;
        var models = new Dictionary<string, string>();
        var total = Stopwatch.StartNew();

        async Task<string> Call(string stage, string agentKey, string promptName, string user, string? fallback = null)
        {
            var agent = ollama.Resolve(agentKey, overrides, fallback);
            models[agentKey] = agent.Model;
            return await ollama.ChatAsync(agent, prompts.Get(promptName), user,
                t => emit(new PipelineEvent("token", stage, t)), ct);
        }

        async Task<string> Agent(string stage, string agentKey, string promptName, string user, string? fallback = null)
        {
            var model = ollama.Resolve(agentKey, overrides, fallback).Model;
            await emit(new PipelineEvent("stage_start", stage, model));
            var sw = Stopwatch.StartNew();
            var text = await Call(stage, agentKey, promptName, user, fallback);
            await emit(new PipelineEvent("stage_end", stage, null, sw.Elapsed.TotalSeconds));
            return text;
        }

        // 1) Turkish → English
        var english = await Agent("translate_in", "Translator", "translator-to-en", req.Question);
        if (string.IsNullOrWhiteSpace(english)) english = req.Question;

        // 2) Router: which agents does this request need?
        var hasProject = !string.IsNullOrWhiteSpace(req.ProjectPath) || req.Files?.Count > 0;
        AgentPlan plan;
        var opt = req.Options;
        if (opt?.Stages is { Count: > 0 } manual)
        {
            bool Has(string s) => manual.Contains(s, StringComparer.OrdinalIgnoreCase);
            plan = new AgentPlan("manual", Has("answer"), Has("analyze"), Has("code"), Has("test"), Has("review"),
                true, "Aşamalar elle seçildi.");
        }
        else if (string.Equals(opt?.Mode, "full", StringComparison.OrdinalIgnoreCase))
        {
            plan = Full("Tam pipeline seçildi.");
        }
        else
        {
            var raw = await Agent("route", "Router", "router",
                $"## REQUEST\n{english}\n\n## PROJECT PROVIDED\n{(hasProject ? "yes" : "no")}", "Translator");
            plan = ParsePlan(raw) ?? Full("Router kararı okunamadı; tam pipeline çalıştırıldı.");
        }
        await emit(new PipelineEvent("plan", "route", plan.Reason, null, plan));

        // 3) Project context (skipped for generic questions that do not need it)
        ProjectContext ctx;
        if (plan.NeedsContext)
        {
            await emit(new PipelineEvent("stage_start", "context", req.ProjectPath));
            var sw0 = Stopwatch.StartNew();
            ctx = contextBuilder.Build(req.ProjectPath, req.Files, $"{english} {req.Question} {req.ExtraContext}");
            await emit(new PipelineEvent("stage_end", "context", ctx.Warning, sw0.Elapsed.TotalSeconds,
                new ContextInfo(ctx.TotalFiles, ctx.Included, ctx.Chars, ctx.Warning)));
        }
        else ctx = contextBuilder.Build(null, null, "");
        var ctxInfo = new ContextInfo(ctx.TotalFiles, ctx.Included, ctx.Chars, ctx.Warning);

        var contextBlock = $"## PROJECT CONTEXT\n{ctx.Text}";
        var extraBlock = string.IsNullOrWhiteSpace(req.ExtraContext)
            ? ""
            : $"\n\n## ADDITIONAL NOTES FROM THE USER (may be in Turkish)\n{req.ExtraContext}";
        var historyBlock = BuildHistory(req.History, cfg.MaxHistoryItems);

        string answer = "", analysis = "", code = "", tests = "", review = "";

        // 4a) Plain answer (questions, explanations)
        if (plan.Answer)
            answer = await Agent("answer", "Analyzer", "answerer",
                $"## QUESTION\n{english}{historyBlock}{extraBlock}\n\n{contextBlock}");

        // 4b) Analysis (only when needed)
        if (plan.Analyze)
            analysis = await Agent("analyze", "Analyzer", "analyzer",
                $"## REQUEST\n{english}{historyBlock}{extraBlock}\n\n{contextBlock}");
        var analysisBlock = analysis.Length > 0 ? $"\n\n## ANALYSIS\n{analysis}" : "";

        // 4c) Coding
        if (plan.Code)
            code = await Agent("code", "Coder", "coder",
                $"## REQUEST\n{english}{analysisBlock}{historyBlock}{extraBlock}\n\n{contextBlock}");
        var implBlock = code.Length > 0
            ? $"\n\n## IMPLEMENTATION\n{code}"
            : "\n\n## IMPLEMENTATION\n(No new code was generated. The code to work on is in the PROJECT CONTEXT / user-provided files.)";

        // 4d) Test → Review (→ revise → test → review ...)
        var approved = true;
        var rounds = 0;
        while (plan.Test || plan.Review)
        {
            if (plan.Test)
                tests = await Agent("test", "Tester", "tester",
                    $"## REQUEST\n{english}{analysisBlock}{implBlock}{extraBlock}\n\n{contextBlock}");

            if (!plan.Review) break;

            review = await Agent("review", "Reviewer", "reviewer",
                $"## REQUEST\n{english}{analysisBlock}{implBlock}\n\n## TESTS\n{(tests.Length > 0 ? tests : "(none)")}{extraBlock}\n\n{contextBlock}");
            approved = IsApproved(review);
            if (approved || !plan.Code || rounds >= maxIter) break;

            rounds++;
            code = await Agent("revise", "Coder", "coder",
                $"## REQUEST\n{english}{analysisBlock}\n\n## PREVIOUS IMPLEMENTATION\n{code}\n\n" +
                $"## REVIEW FEEDBACK\n{review}\n\n" +
                "Produce the corrected, COMPLETE implementation. Fix every BLOCKER and MAJOR issue. " +
                $"Use exactly the same output format.\n\n{contextBlock}");
            implBlock = $"\n\n## IMPLEMENTATION\n{code}";
        }

        // 5) English → Turkish (code blocks are protected and re-inserted verbatim)
        var verdict = approved ? "Onaylandı" : "Değişiklik gerekli";
        var sections = new List<(string Title, string Body)>();
        if (answer.Length > 0) sections.Add(("Cevap", answer));
        if (analysis.Length > 0) sections.Add(("Analiz", analysis));
        if (code.Length > 0) sections.Add(("Uygulama", code));
        if (tests.Length > 0) sections.Add(("Testler", tests));
        if (review.Length > 0)
            sections.Add(($"Code Review — {verdict}" + (rounds > 0 ? $" ({rounds} revizyon turu)" : ""), review));

        var finalMd = new StringBuilder();
        var single = sections.Count == 1;
        string Heading(string title) => single ? "" : $"## {title}\n\n";

        if (translateOut)
        {
            var model = ollama.Resolve("BackTranslator", overrides, "Translator").Model;
            await emit(new PipelineEvent("stage_start", "translate_out", model));
            var sw = Stopwatch.StartNew();
            foreach (var (title, body) in sections)
            {
                finalMd.Append(Heading(title)).Append(await ToTurkish(body)).Append("\n\n");
                await emit(new PipelineEvent("token", "translate_out", "\n"));
            }
            await emit(new PipelineEvent("stage_end", "translate_out", null, sw.Elapsed.TotalSeconds));
        }
        else
        {
            foreach (var (title, body) in sections) finalMd.Append(Heading(title)).Append(body).Append("\n\n");
        }

        async Task<string> ToTurkish(string md)
        {
            var (prot, blocks) = MarkdownUtil.ProtectCode(md);
            if (!MarkdownUtil.HasProse(prot)) return md;
            var tr = await Call("translate_out", "BackTranslator", "translator-to-tr", prot, "Translator");
            return MarkdownUtil.RestoreCode(string.IsNullOrWhiteSpace(tr) ? prot : tr, blocks);
        }

        var files = MarkdownUtil.ExtractFiles(code, "code");
        files.AddRange(MarkdownUtil.ExtractFiles(tests, "test"));

        return new PipelineResult(english, analysis, code, tests, review, approved, rounds,
            finalMd.ToString().TrimEnd(), files, ctxInfo, models, total.Elapsed.TotalSeconds, plan);
    }

    /// <summary>Parses the Router's JSON. Returns null when it cannot be read, so the caller falls back to the full pipeline.</summary>
    private static AgentPlan? ParsePlan(string raw)
    {
        var m = Regex.Match(raw, @"\{.*\}", RegexOptions.Singleline);
        if (!m.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(m.Value);
            var r = doc.RootElement;
            bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            var intent = S("intent");
            var plan = new AgentPlan(intent.Length > 0 ? intent : "custom",
                B("answer"), B("analyze"), B("code"), B("test"), B("review"),
                !r.TryGetProperty("needsContext", out _) || B("needsContext"), S("reason"));

            // Never end up with nothing to run.
            if (!(plan.Answer || plan.Analyze || plan.Code || plan.Test || plan.Review))
                plan = plan with { Answer = true };
            // Analysis alone is just an answer; and an answer never needs code agents on top.
            if (plan.Answer && (plan.Code || plan.Test || plan.Review))
                plan = plan with { Answer = false };
            return plan;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsApproved(string review)
    {
        var head = review.Length > 400 ? review[..400] : review;
        if (Regex.IsMatch(head, @"VERDICT:\s*NEEDS[_ ]CHANGES", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(head, @"VERDICT:\s*APPROVED", RegexOptions.IgnoreCase);
    }

    private static string BuildHistory(List<HistoryItem>? history, int max)
    {
        if (history == null || history.Count == 0 || max <= 0) return "";
        var sb = new StringBuilder("\n\n## CONVERSATION HISTORY (previous turns, oldest first)\n");
        foreach (var h in history.TakeLast(max))
        {
            sb.AppendLine($"Previous request: {h.Request}");
            if (!string.IsNullOrWhiteSpace(h.Code))
                sb.AppendLine($"Previous implementation:\n{MarkdownUtil.Truncate(h.Code, 6000)}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
