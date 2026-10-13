using DevAgents.Application.Context;
using DevAgents.Domain.Agents;
using DevAgents.Domain.Files;

namespace DevAgents.Application.Chat;

/// <summary>The outcome of a pipeline run.</summary>
/// <param name="EnglishRequest">The request as the agents saw it (translated to English).</param>
/// <param name="Approved">Verdict of the last review; true when no review ran.</param>
/// <param name="FinalMarkdown">The report for the user (in Turkish unless translation was turned off).</param>
/// <param name="Files">Code and test files parsed from the coder and tester output.</param>
/// <param name="Models">The model that served each agent.</param>
/// <param name="Seconds">Total duration.</param>
public sealed record PipelineResult(
    string EnglishRequest,
    string Analysis,
    string Code,
    string Tests,
    string Review,
    bool Approved,
    int ReviewRounds,
    string FinalMarkdown,
    IReadOnlyList<GeneratedFile> Files,
    ContextInfo Context,
    IReadOnlyDictionary<string, string> Models,
    double Seconds,
    AgentPlan Plan);
