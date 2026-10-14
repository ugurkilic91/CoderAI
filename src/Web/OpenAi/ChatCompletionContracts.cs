using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevAgents.Web.OpenAi;

// OpenAI chat completions API'sinin cevap biçimleri (yalnızca istemcilerin ihtiyaç duyduğu alanlar).

internal static class OpenAiJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

internal sealed record ChatCompletionChunk(string Id, string Object, long Created, string Model, IReadOnlyList<ChatCompletionChunkChoice> Choices);

internal sealed record ChatCompletionChunkChoice(int Index, ChatCompletionDelta Delta, string? FinishReason);

/// <summary>İçerik yoksa (kapanış parçası) <c>{}</c> olarak yazılır.</summary>
internal sealed record ChatCompletionDelta([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Content);

internal sealed record ChatCompletion(string Id, string Object, long Created, string Model, IReadOnlyList<ChatCompletionChoice> Choices);

internal sealed record ChatCompletionChoice(int Index, ChatCompletionMessage Message, string FinishReason);

internal sealed record ChatCompletionMessage(string Role, string Content);
