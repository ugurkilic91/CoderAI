namespace DevAgents.Domain.Agents;

/// <summary>
/// The agents that take part in the pipeline. The names are also the keys of the per-agent model
/// settings (<c>Ollama:Agents:&lt;name&gt;</c>) and of per-request model overrides.
/// </summary>
public enum AgentRole
{
    Router,
    Translator,
    Analyzer,
    Coder,
    Tester,
    Reviewer,
    BackTranslator
}

public static class AgentRoleExtensions
{
    /// <summary>
    /// The agent whose model settings apply when <paramref name="role"/> has none of its own.
    /// The router and the back-translator are light language tasks, like the translator.
    /// </summary>
    public static AgentRole? SettingsFallback(this AgentRole role) =>
        role is AgentRole.Router or AgentRole.BackTranslator ? AgentRole.Translator : null;
}
