using System.Text;
using DevAgents.Application.Common.Text;
using DevAgents.Application.Context;

namespace DevAgents.Application.Chat;

/// <summary>
/// Builds the user message each agent receives. The "## ..." section names are referenced by the
/// prompts in prompts/*.md, so change both together.
/// </summary>
internal sealed class AgentMessages
{
    private const int MaxHistoryCodeChars = 6000;

    private const string NoImplementation =
        "(No new code was generated. The code to work on is in the PROJECT CONTEXT / user-provided files.)";

    private const string RevisionInstruction =
        "Produce the corrected, COMPLETE implementation. Fix every BLOCKER and MAJOR issue. Use exactly the same output format.";

    private readonly string _request;
    private readonly string _history;
    private readonly string _notes;
    private readonly string _context;

    public AgentMessages(
        string englishRequest,
        ProjectContext context,
        string? extraContext,
        IReadOnlyList<HistoryItem>? history,
        int maxHistoryItems)
    {
        _request = englishRequest;
        _context = $"## PROJECT CONTEXT\n{context.Text}";
        _notes = string.IsNullOrWhiteSpace(extraContext)
            ? ""
            : $"\n\n## ADDITIONAL NOTES FROM THE USER (may be in Turkish)\n{extraContext}";
        _history = HistorySection(history, maxHistoryItems);
    }

    public static string ForRouter(string englishRequest, bool hasProject) =>
        $"## REQUEST\n{englishRequest}\n\n## PROJECT PROVIDED\n{(hasProject ? "yes" : "no")}";

    public string ForAnswer() =>
        $"## QUESTION\n{_request}{_history}{_notes}\n\n{_context}";

    public string ForAnalysis() =>
        $"## REQUEST\n{_request}{_history}{_notes}\n\n{_context}";

    public string ForCoding(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}{_history}{_notes}\n\n{_context}";

    public string ForTesting(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}{Implementation(work)}{_notes}\n\n{_context}";

    public string ForReview(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}{Implementation(work)}\n\n" +
        $"## TESTS\n{(work.Tests.Length > 0 ? work.Tests : "(none)")}{_notes}\n\n{_context}";

    public string ForRevision(AgentOutputs work) =>
        $"## REQUEST\n{_request}{Analysis(work)}\n\n## PREVIOUS IMPLEMENTATION\n{work.Code}\n\n" +
        $"## REVIEW FEEDBACK\n{work.Review}\n\n{RevisionInstruction}\n\n{_context}";

    private static string Analysis(AgentOutputs work) =>
        work.Analysis.Length > 0 ? $"\n\n## ANALYSIS\n{work.Analysis}" : "";

    // Without new code, the tester and reviewer work on the code in the context (e.g. "write tests for X").
    private static string Implementation(AgentOutputs work) =>
        work.Code.Length > 0 || work.IsCodeRevised
            ? $"\n\n## IMPLEMENTATION\n{work.Code}"
            : $"\n\n## IMPLEMENTATION\n{NoImplementation}";

    private static string HistorySection(IReadOnlyList<HistoryItem>? history, int maxItems)
    {
        if (history is null || history.Count == 0 || maxItems <= 0)
            return "";

        var section = new StringBuilder("\n\n## CONVERSATION HISTORY (previous turns, oldest first)\n");
        foreach (var turn in history.Where(turn => turn is not null).TakeLast(maxItems))
        {
            section.AppendLine($"Previous request: {turn.Request}");
            if (!string.IsNullOrWhiteSpace(turn.Code))
                section.AppendLine($"Previous implementation:\n{turn.Code.Truncate(MaxHistoryCodeChars)}");
            section.AppendLine();
        }
        return section.ToString().TrimEnd();
    }
}
