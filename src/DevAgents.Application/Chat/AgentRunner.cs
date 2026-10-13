using System.Diagnostics;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Text;
using DevAgents.Domain.Agents;

namespace DevAgents.Application.Chat;

/// <summary>
/// Calls agents for a single pipeline run: resolves each agent's model (honouring the request's overrides),
/// streams its tokens to the client and records which model served which agent.
/// </summary>
internal sealed class AgentRunner(
    ILanguageModelClient languageModel,
    IAgentModelResolver modelResolver,
    IPromptProvider prompts,
    IReadOnlyDictionary<string, string>? modelOverrides,
    Func<PipelineEvent, Task> onEvent,
    CancellationToken cancellationToken)
{
    private readonly Dictionary<string, string> _usedModels = [];

    /// <summary>Agent name → model, in the order the agents were first called.</summary>
    public IReadOnlyDictionary<string, string> UsedModels => _usedModels;

    public AgentModel ResolveModel(AgentRole role) => modelResolver.Resolve(role, modelOverrides);

    public Task PublishAsync(PipelineEvent pipelineEvent) => onEvent(pipelineEvent);

    /// <summary>Runs an agent as a stage of its own: stage_start (model) → tokens → stage_end (duration).</summary>
    public async Task<string> RunStageAsync(string stage, AgentRole role, string promptName, string userMessage)
    {
        await onEvent(PipelineEvent.StageStarted(stage, ResolveModel(role).Model));
        var stopwatch = Stopwatch.StartNew();
        var answer = await AskAsync(stage, role, promptName, userMessage);
        await onEvent(PipelineEvent.StageEnded(stage, stopwatch.Elapsed.TotalSeconds));
        return answer;
    }

    /// <summary>Calls an agent within an already running <paramref name="stage"/> and returns its answer without reasoning blocks.</summary>
    public async Task<string> AskAsync(string stage, AgentRole role, string promptName, string userMessage)
    {
        var agent = ResolveModel(role);
        _usedModels[agent.Name] = agent.Model;
        var answer = await languageModel.ChatAsync(
            agent,
            prompts.GetPrompt(promptName),
            userMessage,
            token => onEvent(PipelineEvent.Token(stage, token)),
            cancellationToken);
        return ModelOutput.RemoveReasoning(answer);
    }
}
