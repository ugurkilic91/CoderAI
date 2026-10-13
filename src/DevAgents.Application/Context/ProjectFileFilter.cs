using DevAgents.Application.Common.Options;

namespace DevAgents.Application.Context;

/// <summary>Decides which project files are indexed: source-like extensions, no build/tool folders, no secrets.</summary>
public sealed class ProjectFileFilter
{
    private static readonly string[] DefaultExtensions =
    [
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".json", ".md", ".cshtml", ".razor", ".sql",
        ".yml", ".yaml", ".xml", ".config", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".html", ".css", ".proto"
    ];

    private static readonly string[] DefaultExcludedDirectories =
    [
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".vscode", "packages", "dist", "out",
        "TestResults", ".next", "coverage", "__pycache__", ".venv", "venv"
    ];

    private readonly HashSet<string> _extensions;
    private readonly HashSet<string> _excludedDirectories;

    private ProjectFileFilter(IEnumerable<string> extensions, IEnumerable<string> excludedDirectories, int maxFiles, long maxFileBytes)
    {
        _extensions = extensions
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _excludedDirectories = excludedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        MaxFiles = maxFiles;
        MaxFileBytes = maxFileBytes;
    }

    /// <summary>Indexing stops after this many files.</summary>
    public int MaxFiles { get; }

    /// <summary>Larger files are skipped.</summary>
    public long MaxFileBytes { get; }

    public static ProjectFileFilter FromOptions(WorkspaceOptions options) => new(
        options.IncludeExtensions.Length > 0 ? options.IncludeExtensions : DefaultExtensions,
        options.ExcludeDirectories.Length > 0 ? options.ExcludeDirectories : DefaultExcludedDirectories,
        options.MaxFiles,
        options.MaxFileBytes);

    public bool IsExcludedDirectory(string directoryName) => _excludedDirectories.Contains(directoryName);

    public bool HasIncludedExtension(string path) => _extensions.Contains(Path.GetExtension(path));

    /// <summary>Whether the file may reach the agents without the user pinning it.</summary>
    public bool Accepts(string relativePath) =>
        HasIncludedExtension(relativePath) && !SensitiveFiles.IsSensitive(relativePath);
}
