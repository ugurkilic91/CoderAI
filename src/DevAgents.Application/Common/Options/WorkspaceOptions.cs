namespace DevAgents.Application.Common.Options;

/// <summary>Which project files are read and how much of them reaches the agents; bound from "Workspace".</summary>
public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    /// <summary>If empty, any local path is allowed. In Docker, set it to ["/workspace"].</summary>
    public string[] AllowedRoots { get; set; } = [];

    /// <summary>Character budget for the files included in the context.</summary>
    public int MaxContextChars { get; set; } = 24000;

    public int MaxCharsPerFile { get; set; } = 8000;

    /// <summary>Larger files are not indexed.</summary>
    public long MaxFileBytes { get; set; } = 200_000;

    public int MaxFiles { get; set; } = 5000;

    /// <summary>Optional overrides. Empty = built-in defaults.</summary>
    public string[] IncludeExtensions { get; set; } = [];

    public string[] ExcludeDirectories { get; set; } = [];
}
