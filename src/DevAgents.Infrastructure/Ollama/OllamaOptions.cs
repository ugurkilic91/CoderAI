namespace DevAgents.Infrastructure.Ollama;

/// <summary>Ollama connection and per-agent model settings, bound from the "Ollama" configuration section.</summary>
internal sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Maximum duration of one agent call.</summary>
    public int TimeoutMinutes { get; set; } = 20;

    /// <summary>How long Ollama keeps a model loaded after a call.</summary>
    public string KeepAlive { get; set; } = "10m";

    /// <summary>Used for every value an agent does not set itself.</summary>
    public AgentSettings Defaults { get; set; } = new();

    /// <summary>Settings per agent name (see <see cref="Domain.Agents.AgentRole"/>), case-insensitive.</summary>
    public Dictionary<string, AgentSettings> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Model settings of one agent; unset values fall back (see <see cref="OllamaAgentModelResolver"/>).</summary>
internal sealed class AgentSettings
{
    public string? Model { get; set; }

    public double? Temperature { get; set; }

    /// <summary>Context window in tokens (Ollama "num_ctx").</summary>
    public int? NumCtx { get; set; }

    /// <summary>Maximum number of generated tokens (Ollama "num_predict").</summary>
    public int? NumPredict { get; set; }
}
