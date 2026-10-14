using System.Text.RegularExpressions;

namespace DevAgents.Application.Context;

/// <summary>İsteği küçük harfli arama kelimelerine ayırır; kod tanımlayıcıları camelCase/PascalCase'e göre de bölünür.</summary>
internal static partial class QueryTokenizer
{
    private const int MaxTokens = 40;
    private const int MinTokenLength = 3;

    // Neredeyse her istekte geçen, neredeyse her dosyayla eşleşecek kelimeler.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "using", "use", "add", "new", "get", "set", "create", "make",
        "need", "want", "should", "would", "could", "please", "class", "method", "code", "file", "files", "project", "implement",
        "write", "build", "test", "tests", "service", "controller", "have", "has", "are", "was", "will", "can", "all", "any",
        "each", "when", "then", "than", "also", "like", "some", "only", "just", "how", "what", "which", "where", "not", "but"
    };

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        // HashSet'ten eleman silinmediği sürece ekleme sırası korunur; ilk gelen kelimeler öncelik kazanır.
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
