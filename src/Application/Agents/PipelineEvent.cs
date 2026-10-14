using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents;

/// <summary>
/// Akışın ilerleme olayı. İstemcilere satır başına bir JSON nesnesi (NDJSON) olarak gönderilir;
/// web arayüzü ve VS Code eklentisi bu alanları okur, adlarını değiştirmeyin.
/// </summary>
/// <param name="Type"><see cref="PipelineEventTypes"/> değerlerinden biri.</param>
/// <param name="Stage"><see cref="PipelineStages"/> değerlerinden biri.</param>
public sealed record PipelineEvent(string Type, string Stage, string? Text = null, double? Seconds = null, object? Data = null)
{
    /// <summary>Bir aşama başladı; <paramref name="text"/> ajanın modeli (bağlam aşamasında proje yolu).</summary>
    public static PipelineEvent StageStarted(string stage, string? text) =>
        new(PipelineEventTypes.StageStart, stage, text);

    public static PipelineEvent StageEnded(string stage, double seconds, string? text = null, object? data = null) =>
        new(PipelineEventTypes.StageEnd, stage, text, seconds, data);

    /// <summary>Ajanın cevabından, üretildiği anda gelen bir parça.</summary>
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
