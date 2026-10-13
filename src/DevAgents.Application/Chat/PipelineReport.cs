using System.Diagnostics;
using System.Text;
using DevAgents.Application.Common.Text;
using DevAgents.Domain.Agents;

namespace DevAgents.Application.Chat;

/// <summary>Builds the report shown to the user, translated back to Turkish unless translation is turned off.</summary>
internal static class PipelineReport
{
    public static async Task<string> ComposeAsync(AgentOutputs work, bool translate, AgentRunner agents)
    {
        var sections = Sections(work);
        // A report with a single section needs no heading.
        var withHeadings = sections.Count > 1;
        var report = new StringBuilder();

        if (!translate)
        {
            foreach (var (title, body) in sections)
                report.Append(Heading(title, withHeadings)).Append(body).Append("\n\n");
            return report.ToString().TrimEnd();
        }

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

    private static List<(string Title, string Body)> Sections(AgentOutputs work)
    {
        var sections = new List<(string Title, string Body)>();
        if (work.Answer.Length > 0)
            sections.Add(("Cevap", work.Answer));
        if (work.Analysis.Length > 0)
            sections.Add(("Analiz", work.Analysis));
        if (work.Code.Length > 0)
            sections.Add(("Uygulama", work.Code));
        if (work.Tests.Length > 0)
            sections.Add(("Testler", work.Tests));
        if (work.Review.Length > 0)
        {
            var verdict = work.Approved ? "Onaylandı" : "Değişiklik gerekli";
            var rounds = work.ReviewRounds > 0 ? $" ({work.ReviewRounds} revizyon turu)" : "";
            sections.Add(($"Code Review — {verdict}{rounds}", work.Review));
        }
        return sections;
    }

    private static string Heading(string title, bool show) => show ? $"## {title}\n\n" : "";

    // Code blocks are swapped for placeholders and put back verbatim, so the translator cannot alter code.
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
