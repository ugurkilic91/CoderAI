using System.Text.Json;

namespace DevAgents.Api.OpenAi;

/// <summary>The parts of an OpenAI chat completions request the pipeline uses.</summary>
/// <param name="Prompt">Text of the last message; earlier messages are ignored.</param>
internal sealed record ChatCompletionRequest(string Prompt, bool Stream)
{
    /// <summary>Returns <c>null</c> when the body is not a JSON object.</summary>
    public static async Task<ChatCompletionRequest?> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var prompt = root.TryGetProperty("messages", out var messages) &&
                         messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0
                ? ReadText(messages[messages.GetArrayLength() - 1])
                : "";
            // Streams unless the client explicitly asks for a single response.
            var stream = !root.TryGetProperty("stream", out var streamFlag) || streamFlag.ValueKind != JsonValueKind.False;
            return new ChatCompletionRequest(prompt, stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "content" is either a string or a list of parts such as {"type": "text", "text": "..."}.
    private static string ReadText(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content))
            return "";

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? "",
            JsonValueKind.Array => string.Join("\n", content.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object &&
                               part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())),
            _ => ""
        };
    }
}
