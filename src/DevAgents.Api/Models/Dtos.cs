namespace DevAgents.Models;

/// <summary>A file supplied by the client (chat UI attachment or VS Code workspace file).</summary>
public record InlineFile(string Path, string Content, bool Pinned = false);

/// <summary>Which agents run for a request (decided by the Router agent, or forced by the user).</summary>
public record AgentPlan(string Intent, bool Answer, bool Analyze, bool Code, bool Test, bool Review, bool NeedsContext, string Reason);

/// <summary>One previous turn, used so follow-up requests ("now add validation") keep context.</summary>
public record HistoryItem(string? Request, string? Code);

public record RunOptions(
    int? MaxReviewIterations = null,
    Dictionary<string, string>? ModelOverrides = null,
    bool? TranslateOutput = null,
    string? Mode = null,              // "auto" (default, Router decides) | "full" (every agent)
    List<string>? Stages = null);     // manual: any of answer, analyze, code, test, review

public record ChatRequest(
    string Question,
    string? ProjectPath = null,
    string? ExtraContext = null,
    List<InlineFile>? Files = null,
    List<HistoryItem>? History = null,
    RunOptions? Options = null);

public record GeneratedFile(string Path, string Content, string Kind);

public record ContextInfo(int TotalFiles, List<string> IncludedFiles, int Chars, string? Warning);

public record PipelineResult(
    string EnglishRequest,
    string Analysis,
    string Code,
    string Tests,
    string Review,
    bool Approved,
    int ReviewRounds,
    string FinalMarkdown,
    List<GeneratedFile> Files,
    ContextInfo Context,
    Dictionary<string, string> Models,
    double Seconds,
    AgentPlan? Plan = null);

/// <summary>Progress event streamed to clients as NDJSON.</summary>
public record PipelineEvent(string Type, string Stage, string? Text = null, double? Seconds = null, object? Data = null);

public record ContextPreviewRequest(string? ProjectPath, List<InlineFile>? Files, string? Question);
