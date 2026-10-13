using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Chat access to the language model server (Ollama).</summary>
public interface ILanguageModelClient
{
    /// <summary>Base address of the model server, shown in health and configuration responses.</summary>
    string Endpoint { get; }

    /// <summary>Names of the models installed on the server, sorted.</summary>
    /// <exception cref="LanguageModelException">The server cannot be reached or answered with an error.</exception>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends one system + user message pair to <paramref name="agent"/>'s model and streams the answer,
    /// passing every token to <paramref name="onToken"/>. Returns the complete, unprocessed answer.
    /// </summary>
    /// <exception cref="LanguageModelException">The call failed or timed out.</exception>
    Task<string> ChatAsync(
        AgentModel agent,
        string systemPrompt,
        string userMessage,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken);
}
