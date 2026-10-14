using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

internal static partial class ModelOutput
{
    /// <summary>
    /// deepseek-r1 / qwen3 gibi "düşünen" modellerin &lt;think&gt; bloklarını atar ve metni kırpar.
    /// Düşünme metni canlı akışta görünür, ama sonraki ajanlara ve rapora girmez.
    /// </summary>
    public static string RemoveReasoning(string text) => ThinkBlock().Replace(text, "").Trim();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();
}
