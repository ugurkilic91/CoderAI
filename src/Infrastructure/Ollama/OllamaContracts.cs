using System.Text.Json;

namespace DevAgents.Infrastructure.Ollama;

// Ollama REST API'sinin tel biçimi (https://github.com/ollama/ollama/blob/main/docs/api.md).

internal static class OllamaJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

/// <summary>POST /api/chat</summary>
/// <param name="Think">Düşünen modellerde düşünme açık/kapalı; düşünmeyen modeller "false"u yok sayar ("true"da hata verir).</param>
internal sealed record OllamaChatRequest(
    string Model,
    bool Stream,
    string KeepAlive,
    IReadOnlyList<OllamaMessage> Messages,
    OllamaModelOptions Options,
    bool Think);

internal sealed record OllamaMessage(string Role, string Content);

internal sealed record OllamaModelOptions(double Temperature, int NumCtx, int NumPredict);

/// <summary>Akış hâlindeki /api/chat cevabının bir satırı.</summary>
internal sealed record OllamaChatChunk(OllamaMessage? Message, string? Error);

/// <summary>GET /api/tags</summary>
internal sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);

internal sealed record OllamaModelTag(string Name);
