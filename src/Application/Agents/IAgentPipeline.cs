using DevAgents.Application.Agents.Commands.RunAgentPipeline;

namespace DevAgents.Application.Agents;

/// <summary>Çok ajanlı akışın motoru. <see cref="RunAgentPipelineCommandHandler"/> bunu MediatR akışına bağlar.</summary>
public interface IAgentPipeline
{
    /// <summary>
    /// İsteğin ihtiyaç duyduğu ajanları çalıştırır ve ilerlemeyi <paramref name="onEvent"/> ile bildirir
    /// (aşama başlangıcı/bitişi, token'lar, seçilen plan). İstek önceden doğrulanmış olmalıdır.
    /// </summary>
    Task<PipelineResult> RunAsync(RunAgentPipelineCommand request, Func<PipelineEvent, Task> onEvent, CancellationToken cancellationToken);
}
