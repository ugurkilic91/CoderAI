namespace DevAgents.Application.Context;

/// <summary>The architecture-aware context handed to the agents: a project overview plus the most relevant files.</summary>
public sealed class ProjectContext
{
    /// <summary>The context used when neither a project path nor files were given.</summary>
    public static ProjectContext None { get; } = new()
    {
        Overview = "(no project context was provided)",
        Warning = "Proje yolu veya dosya verilmedi; agent'lar genel bilgiyle çalışacak."
    };

    public string Overview { get; init; } = "";

    /// <summary>The selected files as <c>&lt;file path="..."&gt;</c> blocks.</summary>
    public string FilesText { get; init; } = "";

    /// <summary>Paths of the selected files, in the order they appear in <see cref="FilesText"/>.</summary>
    public IReadOnlyList<string> Included { get; init; } = [];

    /// <summary>Number of files that were indexed (before selection).</summary>
    public int TotalFiles { get; init; }

    public string? Warning { get; init; }

    /// <summary>The context as it appears in the agents' messages.</summary>
    public string Text =>
        $"### PROJECT OVERVIEW\n{Overview}\n\n### RELEVANT PROJECT FILES\n{(FilesText.Length == 0 ? "(none)" : FilesText)}";

    public int Chars => Text.Length;

    public ContextInfo ToInfo() => new(TotalFiles, Included, Chars, Warning);
}

/// <summary>Summary of the project context that was handed to the agents.</summary>
public sealed record ContextInfo(int TotalFiles, IReadOnlyList<string> IncludedFiles, int Chars, string? Warning);
