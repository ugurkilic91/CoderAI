using DevAgents.Application.Common.Models;
using DevAgents.Domain.Enums;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Her ajanın hangi model ve örnekleme ayarlarıyla çalışacağını belirler.</summary>
public interface IAgentModelResolver
{
    /// <summary>
    /// Öncelik sırası: istekteki <paramref name="modelOverrides"/> (yalnızca model adı, ajan adına göre)
    /// → ajanın kendi ayarı → <see cref="AgentRoleExtensions.SettingsFallback"/> ajanının ayarı → varsayılanlar.
    /// </summary>
    AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null);
}
