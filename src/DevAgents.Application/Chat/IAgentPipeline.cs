using DevAgents.Application.Common.Exceptions;

namespace DevAgents.Application.Chat;

/// <summary>Runs the multi-agent pipeline for one request.</summary>
public interface IAgentPipeline
{
    /// <summary>
    /// Runs the agents the request needs and reports progress through <paramref name="onEvent"/>
    /// (stage start/end, tokens, the selected plan).
    /// </summary>
    /// <exception cref="InvalidRequestException">The question is empty or the project path cannot be used.</exception>
    Task<PipelineResult> RunAsync(ChatRequest request, Func<PipelineEvent, Task> onEvent, CancellationToken cancellationToken);
}
