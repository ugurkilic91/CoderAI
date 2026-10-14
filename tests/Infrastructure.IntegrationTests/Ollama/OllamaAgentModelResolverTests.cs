using DevAgents.Application.Common.Models;
using DevAgents.Domain.Enums;
using DevAgents.Infrastructure.Ollama;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.Ollama;

public class OllamaAgentModelResolverTests
{
    private static OllamaAgentModelResolver CreateResolver(OllamaOptions options) =>
        new(new StaticOptionsMonitor<OllamaOptions>(options));

    [Test]
    public void ShouldCompleteAgentSettingsFromTheDefaults()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Defaults = new AgentSettings { Model = "default-model", Temperature = 0.3, NumCtx = 2048, NumPredict = 512 },
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["coder"] = new AgentSettings { Model = "coder-model", Temperature = 0.1 } }
        });

        resolver.Resolve(AgentRole.Coder).ShouldBe(new AgentModel("Coder", "coder-model", 0.1, 2048, 512));
    }

    [Test]
    public void ShouldUseTheTranslatorSettingsForARouterWithoutSettings()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["Translator"] = new AgentSettings { Model = "small-model", NumCtx = 4096 } }
        });

        resolver.Resolve(AgentRole.Router).ShouldBe(new AgentModel("Router", "small-model", 0.2, 4096, 4096));
    }

    [Test]
    public void ShouldLetAnOverrideReplaceOnlyTheModel()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["Reviewer"] = new AgentSettings { Model = "reviewer-model", Temperature = 0.05 } }
        });
        var overrides = new Dictionary<string, string> { ["Reviewer"] = "big-model", ["Tester"] = " " };

        resolver.Resolve(AgentRole.Reviewer, overrides).ShouldBe(new AgentModel("Reviewer", "big-model", 0.05, 8192, 4096));
        resolver.Resolve(AgentRole.Tester, overrides).Model.ShouldBe("qwen2.5-coder:7b");
    }

    [Test]
    public void ShouldKeepThinkingOffUnlessAnAgentAsksForIt()
    {
        var resolver = CreateResolver(new OllamaOptions
        {
            Agents = new(StringComparer.OrdinalIgnoreCase) { ["Reviewer"] = new AgentSettings { Model = "deepseek-r1:14b", Think = true } }
        });

        resolver.Resolve(AgentRole.Reviewer).Think.ShouldBeTrue();
        resolver.Resolve(AgentRole.Translator).Think.ShouldBeFalse();
    }

    [Test]
    public void ShouldUseBuiltInFallbacksWhenNothingIsConfigured()
    {
        CreateResolver(new OllamaOptions()).Resolve(AgentRole.Tester)
            .ShouldBe(new AgentModel("Tester", "qwen2.5-coder:7b", 0.2, 8192, 4096));
    }
}
