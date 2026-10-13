using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.Agents;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Ollama;

/// <summary>Resolves agent settings from the "Ollama" configuration section (re-read on every call).</summary>
internal sealed class OllamaAgentModelResolver(IOptionsMonitor<OllamaOptions> options) : IAgentModelResolver
{
    // Used when neither the agent nor the defaults configure a value.
    private const string FallbackModel = "qwen2.5-coder:7b";
    private const double FallbackTemperature = 0.2;
    private const int FallbackNumCtx = 8192;
    private const int FallbackNumPredict = 4096;

    public AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null)
    {
        var settings = options.CurrentValue;
        var name = role.ToString();

        settings.Agents.TryGetValue(name, out var agent);
        if (agent is null && role.SettingsFallback() is { } fallback)
            settings.Agents.TryGetValue(fallback.ToString(), out agent);

        string? overriddenModel = null;
        if (modelOverrides is not null && modelOverrides.TryGetValue(name, out var model) && !string.IsNullOrWhiteSpace(model))
            overriddenModel = model;

        var defaults = settings.Defaults;
        return new AgentModel(
            name,
            overriddenModel ?? agent?.Model ?? defaults.Model ?? FallbackModel,
            agent?.Temperature ?? defaults.Temperature ?? FallbackTemperature,
            agent?.NumCtx ?? defaults.NumCtx ?? FallbackNumCtx,
            agent?.NumPredict ?? defaults.NumPredict ?? FallbackNumPredict);
    }
}
