using System.Text.RegularExpressions;
using DevAgents.Domain.Common;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents.Parsing;

/// <summary>Kodlama ve test ajanlarının yazdığı "### FILE: yol" + kod bloğu çiftlerini ayrıştırır (bkz. prompts/coder.md).</summary>
internal static partial class GeneratedFileParser
{
    /// <summary>Güvensiz yollu dosyalar (mutlak yol, "..") atlanır.</summary>
    public static IEnumerable<GeneratedFile> Parse(string markdown, string kind)
    {
        foreach (Match match in FileBlock().Matches(markdown))
        {
            var path = RelativePath.Normalize(match.Groups["path"].Value);
            if (path is not null)
                yield return new GeneratedFile(path, match.Groups["code"].Value, kind);
        }
    }

    [GeneratedRegex(@"^###[ \t]*FILE:[ \t]*(?<path>[^\n]+?)[ \t]*\r?\n\s*^```[^\n]*\n(?<code>.*?)\r?\n^```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FileBlock();
}
