using System.Text.Json;

namespace DevAgents.Web.Infrastructure;

/// <summary>Satır başına bir JSON (NDJSON) yazar; her satırdan sonra gönderir ki istemci ilerlemeyi anında görsün.</summary>
public sealed class NdjsonStream
{
    private readonly HttpResponse _response;
    private readonly JsonSerializerOptions _serializerOptions;

    private NdjsonStream(HttpResponse response, JsonSerializerOptions serializerOptions)
    {
        _response = response;
        _serializerOptions = serializerOptions;
    }

    public static NdjsonStream Open(HttpResponse response, JsonSerializerOptions serializerOptions)
    {
        response.ContentType = "application/x-ndjson";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no"; // ters vekil sunucular (nginx) akışı biriktirmesin
        return new NdjsonStream(response, serializerOptions);
    }

    public async Task WriteAsync<T>(T value, CancellationToken cancellationToken)
    {
        await _response.WriteAsync(JsonSerializer.Serialize(value, _serializerOptions) + "\n", cancellationToken);
        await _response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>Elden geldiğince yazar: istemci bağlantıyı çoktan kesmiş olabilir.</summary>
    public async Task TryWriteAsync<T>(T value, CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(value, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
