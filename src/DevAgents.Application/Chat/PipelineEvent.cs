using DevAgents.Domain.Agents;

namespace DevAgents.Application.Chat;

/// <summary>A progress event; streamed to clients as one JSON object per line (NDJSON).</summary>
/// <param name="Type">One of <see cref="PipelineEventTypes"/>.</param>
/// <param name="Stage">One of <see cref="PipelineStages"/>.</param>
public sealed record PipelineEvent(string Type, string Stage, string? Text = null, double? Seconds = null, object? Data = null)
{
    /// <summary>A stage started; <paramref name="text"/> is the agent's model (the project path for the context stage).</summary>
    public static PipelineEvent StageStarted(string stage, string? text) =>
        new(PipelineEventTypes.StageStart, stage, text);

    public static PipelineEvent StageEnded(string stage, double seconds, string? text = null, object? data = null) =>
        new(PipelineEventTypes.StageEnd, stage, text, seconds, data);

    /// <summary>A piece of an agent's answer, as it is generated.</summary>
    public static PipelineEvent Token(string stage, string text) =>
        new(PipelineEventTypes.Token, stage, text);

    public static PipelineEvent PlanSelected(AgentPlan plan) =>
        new(PipelineEventTypes.Plan, PipelineStages.Route, plan.Reason, Data: plan);

    public static PipelineEvent Completed(PipelineResult result) =>
        new(PipelineEventTypes.Final, "done", Data: result);

    public static PipelineEvent Failed(string message) =>
        new(PipelineEventTypes.Error, "", message);
}

public static class PipelineEventTypes
{
    public const string StageStart = "stage_start";
    public const string Token = "token";
    public const string StageEnd = "stage_end";
    public const string Plan = "plan";
    public const string Final = "final";
    public const string Error = "error";
}
