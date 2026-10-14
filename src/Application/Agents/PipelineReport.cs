using System.Diagnostics;
using System.Text;
using DevAgents.Application.Agents.Development;
using DevAgents.Application.Agents.Parsing;
using DevAgents.Application.Common.Text;
using DevAgents.Domain.Enums;

namespace DevAgents.Application.Agents;

/// <summary>Raporun ajan çıktısı dışındaki girdileri.</summary>
/// <param name="Questions">Kod yazılmadan önce kullanıcıya sorulan sorular (varsa).</param>
/// <param name="Development">Kod projeye uygulandıysa geliştirme özeti.</param>
/// <param name="CoderNotes">Kodlama ajanının elle yapılacaklar notu (uygulama modunda kod yerine bu gösterilir).</param>
internal sealed record ReportExtras(string Questions = "", DevelopmentSummary? Development = null, string CoderNotes = "")
{
    public static ReportExtras None { get; } = new();
}

/// <summary>Kullanıcıya gösterilen raporu kurar; çeviri kapatılmadıysa Türkçeye çevirir.</summary>
internal static class PipelineReport
{
    public static async Task<string> ComposeAsync(AgentOutputs work, bool translate, AgentRunner agents, ReportExtras extras)
    {
        var sections = Sections(work, extras);
        // Geliştirme özeti kodla Türkçe yazılır, çevrilmez; diğer bölümler ajan çıktısıdır.
        var summary = extras.Development is null ? "" : DevelopmentReport.Render(extras.Development);
        // Tek bölümlük raporda başlığa gerek yok.
        var withHeadings = sections.Count + (summary.Length > 0 ? 1 : 0) > 1;
        var report = new StringBuilder();
        if (summary.Length > 0)
            report.Append(summary).Append("\n\n");

        if (!translate)
        {
            foreach (var (title, body) in sections)
                report.Append(Heading(title, withHeadings)).Append(body).Append("\n\n");
            return report.ToString().TrimEnd();
        }

        // İngilizce → Türkçe: her bölüm ayrı çevrilir, kod blokları korunur.
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.TranslateOut, agents.ResolveModel(AgentRole.BackTranslator).Model));
        var stopwatch = Stopwatch.StartNew();
        foreach (var (title, body) in sections)
        {
            report.Append(Heading(title, withHeadings)).Append(await ToTurkishAsync(body, agents)).Append("\n\n");
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.TranslateOut, "\n"));
        }
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.TranslateOut, stopwatch.Elapsed.TotalSeconds));
        return report.ToString().TrimEnd();
    }

    private static List<(string Title, string Body)> Sections(AgentOutputs work, ReportExtras extras)
    {
        var sections = new List<(string Title, string Body)>();
        // Soru-cevap: ajan kod yazmadan önce kullanıcıya soru sorduysa sorular en üstte.
        if (extras.Questions.Length > 0)
            sections.Add(("Sorular", extras.Questions));
        if (work.Answer.Length > 0)
            sections.Add(("Cevap", work.Answer));
        if (work.Analysis.Length > 0)
        {
            // Sorular (varsa) ayrı bölümde gösterildiği için analizden çıkarılır; soru yoksa ("None") zaten gösterilmez.
            sections.Add(("Analiz", MarkdownSections.Remove(work.Analysis, AnalysisQuestions.SectionTitle)));
        }

        if (extras.Development is null)
        {
            if (work.Code.Length > 0)
                sections.Add(("Uygulama", work.Code));
            if (work.Tests.Length > 0)
                sections.Add(("Testler", work.Tests));
        }
        else if (extras.CoderNotes.Length > 0)
        {
            // Kod zaten projeye yazıldı; raporda kodun kendisi yerine elle yapılacaklar notunu gösteriyoruz.
            sections.Add(("Notlar", extras.CoderNotes));
        }

        if (work.Review.Length > 0)
        {
            var verdict = work.Approved ? "Onaylandı" : "Değişiklik gerekli";
            var rounds = work.ReviewRounds > 0 ? $" ({work.ReviewRounds} revizyon turu)" : "";
            sections.Add(($"Code Review — {verdict}{rounds}", work.Review));
        }
        return sections;
    }

    private static string Heading(string title, bool show) => show ? $"## {title}\n\n" : "";

    // Kod blokları yer tutucuyla değiştirilip çeviriden sonra aynen geri konur; çevirmen kodu bozamaz.
    private static async Task<string> ToTurkishAsync(string markdown, AgentRunner agents)
    {
        var protectedMarkdown = ProtectedMarkdown.Protect(markdown);
        if (!protectedMarkdown.HasProse)
            return markdown;

        var translated = await agents.AskAsync(
            PipelineStages.TranslateOut, AgentRole.BackTranslator, AgentPrompts.TranslatorToTurkish, protectedMarkdown.Text);
        return protectedMarkdown.Restore(string.IsNullOrWhiteSpace(translated) ? protectedMarkdown.Text : translated);
    }
}
