using System.Text.Json;

namespace DevAgents.Api.Endpoints;

/// <summary>Writes newline-delimited JSON, flushing after every line so the client sees progress immediately.</summary>
internal sealed class NdjsonStream
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
        response.Headers["X-Accel-Buffering"] = "no"; // keep reverse proxies (nginx) from buffering the stream
        return new NdjsonStream(response, serializerOptions);
    }

    public async Task WriteAsync<T>(T value, CancellationToken cancellationToken)
    {
        await _response.WriteAsync(JsonSerializer.Serialize(value, _serializerOptions) + "\n", cancellationToken);
        await _response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>Best effort: the client may already be gone.</summary>
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
