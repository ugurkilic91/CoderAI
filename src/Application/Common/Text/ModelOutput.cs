using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

internal static partial class ModelOutput
{
    /// <summary>
    /// deepseek-r1 / qwen3 gibi "düşünen" modellerin &lt;think&gt; bloklarını atar ve metni kırpar.
    /// Düşünme metni canlı akışta görünür, ama sonraki ajanlara ve rapora girmez.
    /// </summary>
    public static string RemoveReasoning(string text) => ThinkBlock().Replace(text, "").Trim();

    /// <summary>
    /// Uzun kod bloklarını kısa bir notla değiştirir; kısa imzalar kalır. Analiz ajanı kuralına rağmen tam kod yazabiliyor:
    /// bu kod sonraki ajanların bağlam penceresini doldurup talimatlarını kestiriyor ve çeviriyi uzatıyordu. Kodu kodlama ajanı yazar.
    /// </summary>
    public static string RemoveLongCodeBlocks(string markdown, int maxLines = 6) =>
        FencedBlock().Replace(markdown, block =>
            block.Value.Count(c => c == '\n') - 1 > maxLines ? "(code omitted)" : block.Value);

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    // ``` ile açılıp kapanan blok (liste içinde girintili olabilir).
    [GeneratedRegex(@"^[ \t]*```[^\n]*\n.*?^[ \t]*```[ \t]*$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex FencedBlock();
}
