namespace DevAgents.Application.Common.Options;

/// <summary>Pipeline behaviour, bound from the "Pipeline" configuration section.</summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>How many times the coder may revise its work after a "needs changes" review.</summary>
    public int MaxReviewIterations { get; set; } = 1;

    /// <summary>How many previous conversation turns are passed to the agents.</summary>
    public int MaxHistoryItems { get; set; } = 2;

    /// <summary>Translate the final report back to Turkish.</summary>
    public bool TranslateOutput { get; set; } = true;
}
