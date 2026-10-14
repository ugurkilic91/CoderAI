using System.Text.Json;
using System.Text.RegularExpressions;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents.Parsing;

/// <summary>Router ajanının JSON kararını okur (biçim prompts/router.md'de tanımlı).</summary>
internal static partial class RouterDecisionParser
{
    /// <summary>Kurallara uydurulmuş planı döndürür; çıktıda okunabilir bir JSON nesnesi yoksa <c>null</c>.</summary>
    public static AgentPlan? Parse(string output)
    {
        var match = JsonObject().Match(output);
        if (!match.Success)
            return null;

        try
        {
            using var document = JsonDocument.Parse(match.Value);
            var decision = document.RootElement;
            var intent = ReadString(decision, "intent");
            var plan = new AgentPlan(
                Intent: intent.Length > 0 ? intent : "custom",
                Answer: ReadFlag(decision, "answer"),
                Analyze: ReadFlag(decision, "analyze"),
                Code: ReadFlag(decision, "code"),
                Test: ReadFlag(decision, "test"),
                Review: ReadFlag(decision, "review"),
                NeedsContext: !decision.TryGetProperty("needsContext", out _) || ReadFlag(decision, "needsContext"),
                Reason: ReadString(decision, "reason"));
            return plan.Normalize();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Yalnızca JSON true bir ajanı açar; "yes", 1 gibi değerler kapalı sayılır.
    private static bool ReadFlag(JsonElement decision, string name) =>
        decision.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement decision, string name) =>
        decision.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // İlk "{" ile son "}" arası: modeller JSON'u bazen açıklama metni ya da kod çiti içine koyuyor.
    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)]
    private static partial Regex JsonObject();
}
