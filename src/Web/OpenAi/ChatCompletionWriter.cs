using System.Text.Json;

namespace DevAgents.Web.OpenAi;

/// <summary>Bir sohbet tamamlamasını ya server-sent events parçaları ya da tek bir JSON cevabı olarak yazar.</summary>
internal sealed class ChatCompletionWriter(HttpResponse response, TimeProvider timeProvider)
{
    /// <summary>İstemcilerin ayarladığı model adı (örn. Continue'nun config.yaml dosyası).</summary>
    public const string ModelId = "csharp-multi-agent";

    private readonly string _id = "chatcmpl-" + Guid.NewGuid().ToString("N");

    public void StartStream()
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    public async Task WriteChunkAsync(string? content, string? finishReason, CancellationToken cancellationToken)
    {
        var chunk = new ChatCompletionChunk(
            _id, "chat.completion.chunk", Now(), ModelId, [new ChatCompletionChunkChoice(0, new ChatCompletionDelta(content), finishReason)]);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, OpenAiJson.Options)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    public async Task WriteDoneAsync(CancellationToken cancellationToken)
    {
        await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    public Task WriteCompletionAsync(string content, CancellationToken cancellationToken)
    {
        var completion = new ChatCompletion(
            _id, "chat.completion", Now(), ModelId, [new ChatCompletionChoice(0, new ChatCompletionMessage("assistant", content), "stop")]);
        response.ContentType = "application/json";
        return response.WriteAsync(JsonSerializer.Serialize(completion, OpenAiJson.Options), cancellationToken);
    }

    public Task WriteErrorAsync(int statusCode, string message, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        return response.WriteAsync(JsonSerializer.Serialize(new { error = message }, OpenAiJson.Options), cancellationToken);
    }

    private long Now() => timeProvider.GetUtcNow().ToUnixTimeSeconds();
}
