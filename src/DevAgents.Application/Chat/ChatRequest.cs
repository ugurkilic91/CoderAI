using DevAgents.Application.Context;

namespace DevAgents.Application.Chat;

/// <summary>A development request: a question (usually Turkish) plus optional project context.</summary>
public sealed record ChatRequest(
    string Question,
    string? ProjectPath = null,
    string? ExtraContext = null,
    IReadOnlyList<InlineFile>? Files = null,
    IReadOnlyList<HistoryItem>? History = null,
    RunOptions? Options = null);

/// <summary>One previous turn, so follow-up requests ("now add validation") keep their context.</summary>
public sealed record HistoryItem(string? Request, string? Code);

/// <summary>Per-request settings that override the configured pipeline behaviour.</summary>
/// <param name="ModelOverrides">Model name per agent name, e.g. <c>{ "Coder": "qwen2.5-coder:14b" }</c>.</param>
/// <param name="Mode">One of <see cref="RunModes"/>; ignored when <paramref name="Stages"/> is set.</param>
/// <param name="Stages">Runs exactly these stages: any of answer, analyze, code, test, review.</param>
public sealed record RunOptions(
    int? MaxReviewIterations = null,
    IReadOnlyDictionary<string, string>? ModelOverrides = null,
    bool? TranslateOutput = null,
    string? Mode = null,
    IReadOnlyList<string>? Stages = null);

public static class RunModes
{
    /// <summary>The Router agent decides which agents run (default).</summary>
    public const string Auto = "auto";

    /// <summary>Every code agent runs: analyze → code → test → review.</summary>
    public const string Full = "full";
}
