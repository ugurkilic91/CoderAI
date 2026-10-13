using System.Text.RegularExpressions;

namespace DevAgents.Application.Common.Text;

/// <summary>
/// Markdown whose code blocks (with their "### FILE:" headers) are replaced by [[CODE_n]] placeholders,
/// so a translator model cannot alter code.
/// </summary>
internal sealed partial class ProtectedMarkdown
{
    private readonly IReadOnlyList<string> _codeBlocks;

    private ProtectedMarkdown(string text, IReadOnlyList<string> codeBlocks)
    {
        Text = text;
        _codeBlocks = codeBlocks;
    }

    /// <summary>The markdown with placeholders instead of code blocks.</summary>
    public string Text { get; }

    /// <summary>Whether enough prose is left to be worth translating.</summary>
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
    /// Puts the code blocks back into <paramref name="translated"/>.
    /// Blocks whose placeholder the translator dropped are appended, so no code is ever lost.
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

        if (missing.Count > 0)
            translated += "\n\n" + string.Join("\n\n", missing);
        return translated;
    }

    // Optional "### FILE: path" header followed by a fenced code block.
    [GeneratedRegex(@"(^###[ \t]*FILE:[^\n]*\n\s*)?^```[^\n]*\n.*?^```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\[\[CODE_\d+\]\]")]
    private static partial Regex Placeholder();
}
