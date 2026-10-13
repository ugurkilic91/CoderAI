using System.Text;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Common.Text;

namespace DevAgents.Application.Context;

/// <summary>Chooses which files go into the context, and in which order, within the character budget.</summary>
internal static class ContextFileSelector
{
    private const int PinnedFileMaxChars = 12_000;
    private const int ProjectFileMaxChars = 2_500;
    private const int EntryPointMaxChars = 4_000;
    private const int ReadmeMaxChars = 1_500;
    private const int MaxProjectFiles = 3;
    private const int MaxEntryPoints = 2;
    private const int MaxFilesPerDirectory = 3;
    private const int MaxExemplars = 8;
    private const int MinExemplarSize = 200;
    private const long MaxScoredFileSize = 60_000;
    private const int PathMatchScore = 6;
    private const int MaxContentMatchesPerToken = 5;

    public static ContextSelection Select(
        IEnumerable<ProjectFile> files, IReadOnlyCollection<ProjectFile> pinned, string query, WorkspaceOptions options)
    {
        var selection = new SelectionBuilder(options.MaxContextChars);

        // 1) files the user explicitly pinned (attachments, active editor, selection), even over budget
        foreach (var file in pinned)
            selection.TryAdd(file, PinnedFileMaxChars, ignoreBudget: true);

        // 2) anchors: csproj, Program/Startup, README
        var rest = files.Where(file => !pinned.Contains(file)).ToList();
        foreach (var file in rest.Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => Depth(f.RelativePath)).Take(MaxProjectFiles))
            selection.TryAdd(file, ProjectFileMaxChars);
        foreach (var file in rest.Where(f => Path.GetFileName(f.RelativePath) is "Program.cs" or "Startup.cs")
                     .OrderBy(f => Depth(f.RelativePath)).Take(MaxEntryPoints))
            selection.TryAdd(file, EntryPointMaxChars);
        foreach (var file in rest.Where(f => Path.GetFileName(f.RelativePath).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                     .Take(1))
            selection.TryAdd(file, ReadmeMaxChars);

        // 3) relevance by keyword overlap with the request, at most a few files per folder
        var perDirectory = new Dictionary<string, int>();
        foreach (var (file, _) in Score(rest, QueryTokenizer.Tokenize(query))
                     .OrderByDescending(scored => scored.Score).ThenBy(scored => scored.File.Size))
        {
            var directory = DirectoryOf(file.RelativePath);
            perDirectory.TryGetValue(directory, out var count);
            if (count >= MaxFilesPerDirectory)
                continue;
            if (selection.TryAdd(file, options.MaxCharsPerFile))
                perDirectory[directory] = count + 1;
        }

        // 4) exemplars: one small source file from each folder not represented yet, to convey conventions
        var represented = selection.Included.Select(DirectoryOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exemplarBudgetFloor = options.MaxContextChars / 5;
        var exemplars = 0;
        foreach (var folder in rest.Where(f => f.RelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && f.Size > MinExemplarSize)
                     .GroupBy(f => DirectoryOf(f.RelativePath))
                     .OrderBy(group => Depth(group.Key)).ThenBy(group => group.Key))
        {
            if (exemplars >= MaxExemplars || selection.RemainingBudget < exemplarBudgetFloor)
                break;
            if (represented.Contains(folder.Key))
                continue;
            if (selection.TryAdd(folder.OrderBy(f => f.Size).First(), options.MaxCharsPerFile))
                exemplars++;
        }

        return new ContextSelection(selection.Text, selection.Included);
    }

    /// <summary>A path match weighs more than a content match; only reasonably small code-like files are searched.</summary>
    private static List<(ProjectFile File, int Score)> Score(IEnumerable<ProjectFile> files, IReadOnlyList<string> tokens)
    {
        var scored = new List<(ProjectFile, int)>();
        foreach (var file in files)
        {
            var path = file.RelativePath.ToLowerInvariant();
            var score = tokens.Count(token => path.Contains(token)) * PathMatchScore;
            if (file.Size <= MaxScoredFileSize && IsCodeLike(file.RelativePath))
            {
                var content = file.ReadContent().ToLowerInvariant();
                score += tokens.Sum(token => CountOccurrences(content, token, MaxContentMatchesPerToken));
            }
            if (score > 0)
                scored.Add((file, score));
        }
        return scored;
    }

    private static int CountOccurrences(string text, string token, int max)
    {
        var count = 0;
        var index = 0;
        while (count < max && (index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static bool IsCodeLike(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".cshtml" or ".razor" or ".ts" or ".tsx" or ".js"
            or ".jsx" or ".py" or ".java" or ".go" or ".sql" or ".md" or ".proto";

    private static int Depth(string path) => path.Count(c => c == '/');

    private static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    /// <summary>Accumulates <c>&lt;file&gt;</c> blocks until the character budget is spent.</summary>
    private sealed class SelectionBuilder(int budget)
    {
        private readonly StringBuilder _text = new();
        private readonly List<string> _included = [];

        public int RemainingBudget { get; private set; } = budget;

        public IReadOnlyList<string> Included => _included;

        public string Text => _text.ToString();

        public bool TryAdd(ProjectFile file, int maxChars, bool ignoreBudget = false)
        {
            if (_included.Contains(file.RelativePath))
                return false;

            var content = file.ReadContent();
            if (content.Length == 0)
                return false;

            var block = $"<file path=\"{file.RelativePath}\">\n{content.Truncate(maxChars)}\n</file>\n";
            if (!ignoreBudget && block.Length > RemainingBudget)
                return false;

            RemainingBudget -= block.Length;
            _text.Append(block);
            _included.Add(file.RelativePath);
            return true;
        }
    }
}

internal sealed record ContextSelection(string Text, IReadOnlyList<string> Included);
