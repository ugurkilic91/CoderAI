using System.Text.RegularExpressions;

namespace DevAgents.Application.Context;

/// <summary>Turns a request into lower-case search keywords; identifiers are also split on camelCase/PascalCase.</summary>
internal static partial class QueryTokenizer
{
    private const int MaxTokens = 40;
    private const int MinTokenLength = 3;

    // Words that appear in almost every request and would match almost every file.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "using", "use", "add", "new", "get", "set", "create", "make",
        "need", "want", "should", "would", "could", "please", "class", "method", "code", "file", "files", "project", "implement",
        "write", "build", "test", "tests", "service", "controller", "have", "has", "are", "was", "will", "can", "all", "any",
        "each", "when", "then", "than", "also", "like", "some", "only", "just", "how", "what", "which", "where", "not", "but"
    };

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        // A HashSet keeps insertion order as long as nothing is removed, so the first tokens win.
        var tokens = new HashSet<string>();
        foreach (Match word in Word().Matches(text ?? ""))
        {
            Add(tokens, word.Value);
            // OrderService -> order, service
            foreach (Match part in WordPart().Matches(word.Value))
                Add(tokens, part.Value);
        }
        return tokens.Take(MaxTokens).ToList();
    }

    private static void Add(HashSet<string> tokens, string word)
    {
        word = word.ToLowerInvariant();
        if (word.Length >= MinTokenLength && !StopWords.Contains(word))
            tokens.Add(word);
    }

    [GeneratedRegex("[A-Za-z][A-Za-z0-9]{2,}")]
    private static partial Regex Word();

    [GeneratedRegex("[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")]
    private static partial Regex WordPart();
}
