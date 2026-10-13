namespace DevAgents.Application.Chat;

/// <summary>
/// Stage names used in <see cref="PipelineEvent.Stage"/> (the web UI and the VS Code extension display them).
/// <see cref="Answer"/>, <see cref="Analyze"/>, <see cref="Code"/>, <see cref="Test"/> and <see cref="Review"/>
/// are also the values accepted by <see cref="RunOptions.Stages"/>.
/// </summary>
public static class PipelineStages
{
    public const string TranslateIn = "translate_in";
    public const string Route = "route";
    public const string Context = "context";
    public const string Answer = "answer";
    public const string Analyze = "analyze";
    public const string Code = "code";
    public const string Test = "test";
    public const string Review = "review";
    public const string Revise = "revise";
    public const string TranslateOut = "translate_out";
}
