namespace DevAgents.Options;

public class AgentSettings
{
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? NumCtx { get; set; }
    public int? NumPredict { get; set; }
}

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public int TimeoutMinutes { get; set; } = 20;
    public string KeepAlive { get; set; } = "10m";
    public AgentSettings Defaults { get; set; } = new();
    public Dictionary<string, AgentSettings> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PipelineSettings
{
    public int MaxReviewIterations { get; set; } = 1;
    public int MaxHistoryItems { get; set; } = 2;
    public bool TranslateOutput { get; set; } = true;
}

public class WorkspaceOptions
{
    /// <summary>If empty, any local path is allowed. In Docker, set it to ["/workspace"].</summary>
    public string[] AllowedRoots { get; set; } = [];
    public int MaxContextChars { get; set; } = 24000;
    public int MaxCharsPerFile { get; set; } = 8000;
    public long MaxFileBytes { get; set; } = 200_000;
    public int MaxFiles { get; set; } = 5000;
    /// <summary>Optional overrides. Empty = built-in defaults.</summary>
    public string[] IncludeExtensions { get; set; } = [];
    public string[] ExcludeDirectories { get; set; } = [];
}
