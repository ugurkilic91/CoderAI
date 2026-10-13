using System.Text.RegularExpressions;
using DevAgents.Domain.Files;

namespace DevAgents.Application.Chat.Parsing;

/// <summary>Extracts the "### FILE: path" + fenced code block pairs written by the coder and tester agents (see prompts/coder.md).</summary>
internal static partial class GeneratedFileParser
{
    /// <summary>Files with unsafe paths (absolute, "..") are skipped.</summary>
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
