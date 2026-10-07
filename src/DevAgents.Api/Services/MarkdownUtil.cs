using System.Text;
using System.Text.RegularExpressions;
using DevAgents.Models;

namespace DevAgents.Services;

public static class MarkdownUtil
{
    // Optional "### FILE: path" header followed by a fenced code block.
    private static readonly Regex FenceWithHeader = new(
        @"(^###[ \t]*FILE:[^\n]*\n\s*)?^```[^\n]*\n.*?^```[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex FileBlock = new(
        @"^###[ \t]*FILE:[ \t]*(?<path>[^\n]+?)[ \t]*\r?\n\s*^```[^\n]*\n(?<code>.*?)\r?\n^```[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Think = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\[\[CODE_\d+\]\]", RegexOptions.Compiled);

    /// <summary>Removes reasoning blocks emitted by models such as deepseek-r1 / qwen3.</summary>
    public static string StripThinking(string s) => Think.Replace(s, "").Trim();

    /// <summary>Replaces code blocks (and their FILE headers) with [[CODE_n]] so the translator can't touch code.</summary>
    public static (string Text, List<string> Blocks) ProtectCode(string md)
    {
        var blocks = new List<string>();
        var text = FenceWithHeader.Replace(md, m =>
        {
            blocks.Add(m.Value);
            return $"[[CODE_{blocks.Count}]]";
        });
        return (text, blocks);
    }

    public static bool HasProse(string protectedText) =>
        Placeholder.Replace(protectedText, "").Trim().Length > 20;

    /// <summary>Puts code blocks back. Blocks the translator dropped are appended so no code is ever lost.</summary>
    public static string RestoreCode(string text, List<string> blocks)
    {
        var missing = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var token = $"[[CODE_{i + 1}]]";
            if (text.Contains(token)) text = text.Replace(token, blocks[i]);
            else missing.Add(blocks[i]);
        }
        if (missing.Count > 0)
            text += "\n\n" + string.Join("\n\n", missing);
        return text;
    }

    /// <summary>Parses "### FILE: path" + fenced block pairs produced by the coder / tester agents.</summary>
    public static List<GeneratedFile> ExtractFiles(string md, string kind)
    {
        var list = new List<GeneratedFile>();
        foreach (Match m in FileBlock.Matches(md))
        {
            var path = CleanPath(m.Groups["path"].Value);
            if (path == null) continue;
            list.Add(new GeneratedFile(path, m.Groups["code"].Value, kind));
        }
        return list;
    }

    public static string? CleanPath(string raw)
    {
        var p = raw.Trim().Trim('`', '"', '\'', '*').Replace('\\', '/').TrimStart('/');
        while (p.StartsWith("./")) p = p[2..];
        if (p.Length == 0 || p.Contains("..") || p.Contains(':')) return null;
        return p;
    }

    public static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n... [truncated]";
}
