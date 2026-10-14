using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.Agents.Development;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevAgents.Application.UnitTests.Fakes;

/// <summary>Ajan akışını tüm portları sahte olarak kurar ve yayımlanan olayları toplar.</summary>
internal sealed class PipelineHarness
{
    public PipelineHarness()
    {
        Writer = new FakeProjectWriter(Workspace);
        Tracker = new FakeChangeTracker(Workspace);
        UsingFixer = new FakeUsingFixer(Workspace);
    }

    public FakeLanguageModel Model { get; } = new FakeLanguageModel().Answer("translator-to-en", "Add an order cancel endpoint");

    public FakeWorkspace Workspace { get; } = new();

    public FakeBuildSandbox Sandbox { get; } = new();

    public FakeProjectWriter Writer { get; }

    public FakeChangeTracker Tracker { get; }

    public FakeUsingFixer UsingFixer { get; }

    public PipelineOptions PipelineOptions { get; } = new() { TranslateOutput = false };

    public DevelopmentOptions DevelopmentOptions { get; } = new();

    public List<PipelineEvent> Events { get; } = [];

    public IEnumerable<string> StartedStages =>
        Events.Where(e => e.Type == PipelineEventTypes.StageStart).Select(e => e.Stage);

    public Task<PipelineResult> RunAsync(RunAgentPipelineCommand request)
    {
        var developmentOptions = new StaticOptionsMonitor<DevelopmentOptions>(DevelopmentOptions);
        var development = new DevelopmentWorkflow(
            Writer, Tracker, new FakeSyntaxChecker(), UsingFixer, new FakeMemberRestorer(), Sandbox, Workspace, new ProjectLocks(), developmentOptions,
            NullLogger<DevelopmentWorkflow>.Instance);
        var pipeline = new AgentPipeline(
            Model,
            new FakeModelResolver(),
            new FakePromptProvider(),
            new ProjectContextBuilder(Workspace, new StaticOptionsMonitor<WorkspaceOptions>(new WorkspaceOptions())),
            new FakeArchitectureCatalog(),
            Workspace,
            development,
            new StaticOptionsMonitor<PipelineOptions>(PipelineOptions),
            developmentOptions,
            NullLogger<AgentPipeline>.Instance);

        return pipeline.RunAsync(request, pipelineEvent =>
        {
            Events.Add(pipelineEvent);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }
}
