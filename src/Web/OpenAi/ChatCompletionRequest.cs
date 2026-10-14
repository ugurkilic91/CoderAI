using System.Text.Json;

namespace DevAgents.Web.OpenAi;

/// <summary>OpenAI chat completions isteğinin akışın kullandığı kısmı.</summary>
/// <param name="Prompt">Son mesajın metni; önceki mesajlar kullanılmaz.</param>
internal sealed record ChatCompletionRequest(string Prompt, bool Stream)
{
    /// <summary>Gövde bir JSON nesnesi değilse <c>null</c> döner.</summary>
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
            // İstemci açıkça tek cevap istemedikçe ("stream": false) cevap akış olarak gider.
            var stream = !root.TryGetProperty("stream", out var streamFlag) || streamFlag.ValueKind != JsonValueKind.False;
            return new ChatCompletionRequest(prompt, stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "content" ya bir metin ya da {"type": "text", "text": "..."} parçalarından oluşan bir liste olabilir.
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
