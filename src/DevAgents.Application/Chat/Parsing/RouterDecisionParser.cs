using System.Text.Json;
using System.Text.RegularExpressions;
using DevAgents.Domain.Agents;

namespace DevAgents.Application.Chat.Parsing;

/// <summary>Reads the router agent's JSON decision (format defined in prompts/router.md).</summary>
internal static partial class RouterDecisionParser
{
    /// <summary>Returns the normalized plan, or <c>null</c> when the output holds no readable JSON object.</summary>
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

    private static bool ReadFlag(JsonElement decision, string name) =>
        decision.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement decision, string name) =>
        decision.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // From the first "{" to the last "}": models sometimes wrap the JSON in prose or code fences.
    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)]
    private static partial Regex JsonObject();
}
