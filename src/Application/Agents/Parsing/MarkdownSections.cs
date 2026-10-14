using System.Text.RegularExpressions;

namespace DevAgents.Application.Agents.Parsing;

/// <summary>Ajan çıktısındaki "### Başlık" bölümlerini okur.</summary>
internal static partial class MarkdownSections
{
    /// <summary>
    /// Başlığı <paramref name="title"/> ile başlayan ilk "###" bölümünün gövdesi (bir sonraki "###" başlığına kadar);
    /// bölüm yoksa boş metin.
    /// </summary>
    public static string Read(string markdown, string title)
    {
        var match = Heading().Matches(markdown).FirstOrDefault(m => m.Groups["title"].Value.Trim()
            .StartsWith(title, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return "";

        var start = match.Index + match.Length;
        var next = Heading().Match(markdown, start);
        var end = next.Success ? next.Index : markdown.Length;
        return markdown[start..end].Trim();
    }

    /// <summary>Başlığı <paramref name="title"/> ile başlayan bölümü (başlığıyla birlikte) metinden çıkarır.</summary>
    public static string Remove(string markdown, string title)
    {
        var match = Heading().Matches(markdown).FirstOrDefault(m => m.Groups["title"].Value.Trim()
            .StartsWith(title, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return markdown;

        var next = Heading().Match(markdown, match.Index + match.Length);
        var end = next.Success ? next.Index : markdown.Length;
        return (markdown[..match.Index] + markdown[end..]).Trim();
    }

    [GeneratedRegex(@"^###[ \t]+(?<title>[^\n]+)$\n?", RegexOptions.Multiline)]
    private static partial Regex Heading();
}

/// <summary>Analiz ajanının "### Questions for the user" bölümü (bkz. prompts/analyzer.md).</summary>
internal static class AnalysisQuestions
{
    public const string SectionTitle = "Questions for the user";

    /// <summary>Kullanıcıya sorulacak sorular; ajan soru sormadıysa ("None") boş metin.</summary>
    public static string Extract(string analysis)
    {
        var questions = MarkdownSections.Read(analysis, SectionTitle);
        var normalized = questions.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized is "" or "none" or "n/a" or "-" or "no questions" ? "" : questions;
    }
}
