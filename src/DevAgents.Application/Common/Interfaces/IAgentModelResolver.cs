using DevAgents.Application.Common.Models;
using DevAgents.Domain.Agents;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Resolves the model and sampling settings each agent runs with.</summary>
public interface IAgentModelResolver
{
    /// <summary>
    /// Resolution order: <paramref name="modelOverrides"/> (model name only, keyed by agent name)
    /// → the agent's own settings → the settings of its <see cref="AgentRoleExtensions.SettingsFallback"/> agent
    /// → the defaults.
    /// </summary>
    AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null);
}
