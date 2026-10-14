using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

/// <summary>
/// Kod blokları ("### FILE:" başlıklarıyla birlikte) [[CODE_n]] yer tutucularıyla değiştirilmiş Markdown.
/// Çeviri modeli kodu göremediği için bozamaz; çeviriden sonra bloklar aynen geri konur.
/// </summary>
internal sealed partial class ProtectedMarkdown
{
    private readonly IReadOnlyList<string> _codeBlocks;

    private ProtectedMarkdown(string text, IReadOnlyList<string> codeBlocks)
    {
        Text = text;
        _codeBlocks = codeBlocks;
    }

    /// <summary>Kod blokları yerine yer tutucuların olduğu metin.</summary>
    public string Text { get; }

    /// <summary>Çevirmeye değecek kadar düz metin kaldı mı (yer tutucular dışında 20 karakterden fazla).</summary>
    public bool HasProse => Placeholder().Replace(Text, "").Trim().Length > 20;

    public static ProtectedMarkdown Protect(string markdown)
    {
        var blocks = new List<string>();
        var text = CodeBlock().Replace(markdown, match =>
        {
            blocks.Add(match.Value);
            return $"[[CODE_{blocks.Count}]]";
        });
        return new ProtectedMarkdown(text, blocks);
    }

    /// <summary>
    /// Kod bloklarını çevrilmiş metne geri koyar. Çevirmenin kaybettiği yer tutucuların blokları
    /// metnin sonuna eklenir; böylece hiçbir kod kaybolmaz.
    /// </summary>
    public string Restore(string translated)
    {
        var missing = new List<string>();
        for (var i = 0; i < _codeBlocks.Count; i++)
        {
            var placeholder = $"[[CODE_{i + 1}]]";
            if (translated.Contains(placeholder))
                translated = translated.Replace(placeholder, _codeBlocks[i]);
            else
                missing.Add(_codeBlocks[i]);
        }

        // Çevirmenin uydurduğu ya da başka bölümden taşıdığı yer tutucular (karşılığı olan blok yok) metinde kalmasın.
        translated = Placeholder().Replace(translated, "").TrimEnd();

        if (missing.Count > 0)
            translated += "\n\n" + string.Join("\n\n", missing);
        return translated;
    }

    // İsteğe bağlı "### FILE: yol" başlığı ve ardından gelen ``` kod bloğu (liste içindeyse girintili olabilir).
    [GeneratedRegex(@"(^###[ \t]*FILE:[^\n]*\n\s*)?^[ \t]*```[^\n]*\n.*?^[ \t]*```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\[\[CODE_\d+\]\]")]
    private static partial Regex Placeholder();
}
