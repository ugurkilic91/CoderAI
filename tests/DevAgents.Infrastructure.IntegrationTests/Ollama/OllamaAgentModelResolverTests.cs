using DevAgents.Application.Common.Models;
using DevAgents.Domain.Agents;
using DevAgents.Infrastructure.Ollama;

namespace DevAgents.Infrastructure.IntegrationTests.Ollama;

public class OllamaAgentModelResolverTests
{
    private static OllamaAgentModelResolver CreateResolver(OllamaOptions options) =>
        new(new StaticOptionsMonitor<OllamaOptions>(options));

    [Test]
    public void Resolve_AgentSettings_AreCompletedFromTheDefaults()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Defaults = new AgentSettings { Model = "default-model", Temperature = 0.3, NumCtx = 2048, NumPredict = 512 },
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["coder"] = new AgentSettings { Model = "coder-model", Temperature = 0.1 } }
        });

        Assert.That(resolver.Resolve(AgentRole.Coder), Is.EqualTo(new AgentModel("Coder", "coder-model", 0.1, 2048, 512)));
    }

    [Test]
    public void Resolve_RouterWithoutSettings_UsesTheTranslatorSettings()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["Translator"] = new AgentSettings { Model = "small-model", NumCtx = 4096 } }
        });

        Assert.That(resolver.Resolve(AgentRole.Router), Is.EqualTo(new AgentModel("Router", "small-model", 0.2, 4096, 4096)));
    }

    [Test]
    public void Resolve_Override_ReplacesOnlyTheModel()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["Reviewer"] = new AgentSettings { Model = "reviewer-model", Temperature = 0.05 } }
        });
        var overrides = new Dictionary<string, string> { ["Reviewer"] = "big-model", ["Tester"] = " " };

        Assert.That(resolver.Resolve(AgentRole.Reviewer, overrides), Is.EqualTo(new AgentModel("Reviewer", "big-model", 0.05, 8192, 4096)));
        Assert.That(resolver.Resolve(AgentRole.Tester, overrides).Model, Is.EqualTo("qwen2.5-coder:7b"));
    }

    [Test]
    public void Resolve_NothingConfigured_UsesBuiltInFallbacks()
    {
        Assert.That(CreateResolver(new OllamaOptions()).Resolve(AgentRole.Tester),
            Is.EqualTo(new AgentModel("Tester", "qwen2.5-coder:7b", 0.2, 8192, 4096)));
    }
}
