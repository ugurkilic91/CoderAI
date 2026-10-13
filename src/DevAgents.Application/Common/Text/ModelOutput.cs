using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

internal static partial class ModelOutput
{
    /// <summary>Removes reasoning blocks emitted by models such as deepseek-r1 / qwen3, and trims the text.</summary>
    public static string RemoveReasoning(string text) => ThinkBlock().Replace(text, "").Trim();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();
}
