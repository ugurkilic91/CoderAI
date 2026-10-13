using System.Text.Json;

namespace DevAgents.Infrastructure.Ollama;

// Wire format of the Ollama REST API (https://github.com/ollama/ollama/blob/main/docs/api.md).

internal static class OllamaJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

/// <summary>POST /api/chat</summary>
internal sealed record OllamaChatRequest(
    string Model,
    bool Stream,
    string KeepAlive,
    IReadOnlyList<OllamaMessage> Messages,
    OllamaModelOptions Options);

internal sealed record OllamaMessage(string Role, string Content);

internal sealed record OllamaModelOptions(double Temperature, int NumCtx, int NumPredict);

/// <summary>One line of the streamed /api/chat response.</summary>
internal sealed record OllamaChatChunk(OllamaMessage? Message, string? Error);

/// <summary>GET /api/tags</summary>
internal sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);

internal sealed record OllamaModelTag(string Name);
