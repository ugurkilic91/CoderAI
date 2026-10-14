using System.Diagnostics;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Text;
using DevAgents.Domain.Enums;

namespace DevAgents.Application.Agents;

/// <summary>
/// Tek bir akış çalıştırması için ajanları çağırır: her ajanın modelini (istekteki seçimlere göre) belirler,
/// cevabın parçalarını istemciye canlı iletir ve hangi modelin hangi ajana hizmet ettiğini kaydeder.
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

    /// <summary>Ajan adı → model; ajanların ilk çağrılma sırasıyla.</summary>
    public IReadOnlyDictionary<string, string> UsedModels => _usedModels;

    public AgentModel ResolveModel(AgentRole role) => modelResolver.Resolve(role, modelOverrides);

    public Task PublishAsync(PipelineEvent pipelineEvent) => onEvent(pipelineEvent);

    /// <summary>Ajanı kendi aşaması olarak çalıştırır: stage_start (model) → token'lar → stage_end (süre).</summary>
    public async Task<string> RunStageAsync(string stage, AgentRole role, string promptName, string userMessage)
    {
        await onEvent(PipelineEvent.StageStarted(stage, ResolveModel(role).Model));
        var stopwatch = Stopwatch.StartNew();
        var answer = await AskAsync(stage, role, promptName, userMessage);
        await onEvent(PipelineEvent.StageEnded(stage, stopwatch.Elapsed.TotalSeconds));
        return answer;
    }

    /// <summary>
    /// Ajanı, zaten başlamış bir aşamanın içinde çağırır (örn. raporun bölüm bölüm çevrilmesi).
    /// Cevaptaki düşünme (&lt;think&gt;) blokları atılarak döner.
    /// </summary>
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
