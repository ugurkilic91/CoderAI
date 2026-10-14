using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;

namespace DevAgents.Application.UnitTests.Fakes;

/// <summary>
/// Sistem prompt'una göre önceden yazılmış cevaplar verir (<see cref="FakePromptProvider"/> ile sistem prompt'u
/// prompt'un adıdır) ve her çağrıyı kaydeder. Son cevap tekrar eder; tanımsız prompt'lar "" alır.
/// </summary>
internal sealed class FakeLanguageModel : ILanguageModelClient
{
    private readonly Dictionary<string, Queue<string>> _answers = [];

    public List<ModelCall> Calls { get; } = [];

    public string Endpoint => "http://ollama.test";

    public FakeLanguageModel Answer(string promptName, params string[] answers)
    {
        _answers[promptName] = new Queue<string>(answers);
        return this;
    }

    public IEnumerable<ModelCall> CallsTo(string promptName) => Calls.Where(call => call.SystemPrompt == promptName);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["model-a", "model-b"]);

    public async Task<string> ChatAsync(
        AgentModel agent, string systemPrompt, string userMessage, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        Calls.Add(new ModelCall(agent, systemPrompt, userMessage));
        var answer = "";
        if (_answers.TryGetValue(systemPrompt, out var queue) && queue.Count > 0)
            answer = queue.Count > 1 ? queue.Dequeue() : queue.Peek();

        // Gerçek model gibi cevabı parça parça (iki parça) akıtıyoruz.
        if (onToken is not null && answer.Length > 0)
        {
            await onToken(answer[..(answer.Length / 2)]);
            await onToken(answer[(answer.Length / 2)..]);
        }
        return answer;
    }
}

internal sealed record ModelCall(AgentModel Agent, string SystemPrompt, string UserMessage);
