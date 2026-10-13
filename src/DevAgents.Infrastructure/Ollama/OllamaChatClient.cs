using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Ollama;

/// <summary>Talks to Ollama's REST API; chat answers are streamed as NDJSON.</summary>
internal sealed class OllamaChatClient(HttpClient http, IOptionsMonitor<OllamaOptions> options) : ILanguageModelClient
{
    public string Endpoint => options.CurrentValue.BaseUrl.TrimEnd('/');

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var endpoint = Endpoint;
        try
        {
            using var response = await http.GetAsync($"{endpoint}/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();
            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(OllamaJson.Options, cancellationToken);
            if (tags?.Models is null)
                throw new LanguageModelException($"Ollama'dan beklenmeyen yanıt alındı ({endpoint}/api/tags).");
            return [.. tags.Models.Select(model => model.Name).OrderBy(name => name)];
        }
        catch (HttpRequestException ex)
        {
            throw new LanguageModelException($"Ollama'ya bağlanılamadı ({endpoint}): {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new LanguageModelException($"Ollama'dan beklenmeyen yanıt alındı ({endpoint}/api/tags): {ex.Message}", ex);
        }
    }

    public async Task<string> ChatAsync(
        AgentModel agent, string systemPrompt, string userMessage, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var endpoint = Endpoint;
        var payload = new OllamaChatRequest(
            agent.Model,
            Stream: true,
            settings.KeepAlive,
            [new OllamaMessage("system", systemPrompt), new OllamaMessage("user", userMessage)],
            new OllamaModelOptions(agent.Temperature, agent.NumCtx, agent.NumPredict));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(settings.TimeoutMinutes));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/chat")
        {
            // StringContent (not JsonContent) so the request carries Content-Length instead of chunked encoding
            Content = new StringContent(JsonSerializer.Serialize(payload, OllamaJson.Options), Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await SendAsync(request, endpoint, timeout.Token);
            await EnsureSuccessAsync(response, agent, timeout.Token);
            return await ReadAnswerAsync(response, agent, onToken, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanguageModelTimeoutException(
                $"{agent.Name} ({agent.Model}) {settings.TimeoutMinutes} dakika içinde yanıt vermedi. " +
                "Ollama:TimeoutMinutes değerini artırabilirsiniz.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LanguageModelException($"Ollama'ya bağlanılamadı ({endpoint}): {ex.Message}. Ollama çalışıyor mu?", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, AgentModel agent, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var hint = response.StatusCode == HttpStatusCode.NotFound
            ? $" Model yüklü olmayabilir: 'ollama pull {agent.Model}'"
            : "";
        throw new LanguageModelException(
            $"Ollama hatası {(int)response.StatusCode} ({agent.Name} / {agent.Model}): {body}{hint}");
    }

    private static async Task<string> ReadAnswerAsync(
        HttpResponseMessage response, AgentModel agent, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        var answer = new StringBuilder();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, OllamaJson.Options);
                if (chunk?.Error is { } error)
                    throw new LanguageModelException($"Ollama ({agent.Model}): {error}");

                var token = chunk?.Message?.Content;
                if (string.IsNullOrEmpty(token))
                    continue;
                answer.Append(token);
                if (onToken is not null)
                    await onToken(token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            throw new LanguageModelException($"Ollama yanıtı okunamadı ({agent.Name} / {agent.Model}): {ex.Message}", ex);
        }
        return answer.ToString();
    }
}
