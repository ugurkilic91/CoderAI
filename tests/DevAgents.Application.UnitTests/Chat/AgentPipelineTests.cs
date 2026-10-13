using DevAgents.Application.Chat;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Application.UnitTests.Fakes;
using DevAgents.Domain.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevAgents.Application.UnitTests.Chat;

public class AgentPipelineTests
{
    private const string FeatureDecision =
        """{"intent":"feature","analyze":true,"code":true,"test":true,"review":true,"needsContext":true,"reason":"Yeni özellik."}""";
    private const string AnswerDecision = """{"intent":"question","answer":true,"needsContext":false,"reason":"Soru."}""";
    private const string CoderAnswer = "### FILE: src/Orders/CancelOrder.cs\n```csharp\nclass CancelOrder {}\n```\n\n### NOTES\n- none";
    private const string RevisedCoderAnswer = "### FILE: src/Orders/CancelOrder.cs\n```csharp\nclass CancelOrder { /* guarded */ }\n```";
    private const string TesterAnswer = "### FILE: tests/CancelOrderTests.cs\n```csharp\nclass CancelOrderTests {}\n```";

    private FakeLanguageModel _model = null!;
    private List<PipelineEvent> _events = null!;

    [SetUp]
    public void SetUp()
    {
        _model = new FakeLanguageModel().Answer("translator-to-en", "Add an order cancel endpoint");
        _events = [];
    }

    private IEnumerable<string> StartedStages =>
        _events.Where(e => e.Type == PipelineEventTypes.StageStart).Select(e => e.Stage);

    private Task<PipelineResult> RunAsync(ChatRequest request, PipelineOptions? options = null, FakeWorkspace? workspace = null)
    {
        var contextBuilder = new ProjectContextBuilder(
            workspace ?? new FakeWorkspace(), new StaticOptionsMonitor<WorkspaceOptions>(new WorkspaceOptions()));
        var pipeline = new AgentPipeline(
            _model,
            new FakeModelResolver(),
            new FakePromptProvider(),
            contextBuilder,
            new StaticOptionsMonitor<PipelineOptions>(options ?? new PipelineOptions { TranslateOutput = false }),
            NullLogger<AgentPipeline>.Instance);

        return pipeline.RunAsync(request, pipelineEvent =>
        {
            _events.Add(pipelineEvent);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RunAsync_EmptyQuestion_IsRejectedBeforeAnyModelCall()
    {
        var exception = await Assert.ThrowsAsync<InvalidRequestException>(() => RunAsync(new ChatRequest("  ")));

        Assert.That(exception!.Message, Is.EqualTo("Soru boş olamaz."));
        Assert.That(_model.Calls, Is.Empty);
    }

    [Test]
    public async Task RunAsync_RouterChoosesAnswer_RunsOnlyTheAnswerAgent()
    {
        _model.Answer("router", "<think>easy</think>" + AnswerDecision)
            .Answer("answerer", "A record is an immutable reference type.");

        var result = await RunAsync(new ChatRequest("record nedir?"));

        Assert.That(StartedStages, Is.EqualTo(new[] { "translate_in", "route", "answer" }));
        Assert.That(result.Plan.Intent, Is.EqualTo("question"));
        // A single section is reported without a heading.
        Assert.That(result.FinalMarkdown, Is.EqualTo("A record is an immutable reference type."));
        Assert.That(result.Context.Warning, Is.EqualTo(ProjectContext.None.Warning));
    }

    [Test]
    public async Task RunAsync_ReviewerAsksForChanges_CoderRevisesUntilApproved()
    {
        _model.Answer("router", FeatureDecision)
            .Answer("analyzer", "### Goal\nCancel orders.")
            .Answer("coder", CoderAnswer, RevisedCoderAnswer)
            .Answer("tester", TesterAnswer)
            .Answer("reviewer", "VERDICT: NEEDS_CHANGES\n### Issues\n[MAJOR] missing guard", "VERDICT: APPROVED");
        var workspace = new FakeWorkspace().With("Shop.csproj", "<Project />");

        var result = await RunAsync(new ChatRequest("Sipariş iptali ekle", ProjectPath: "Shop"), workspace: workspace);

        Assert.That(StartedStages, Is.EqualTo(new[]
        {
            "translate_in", "route", "context", "analyze", "code", "test", "review", "revise", "test", "review"
        }));
        Assert.That(result.Approved, Is.True);
        Assert.That(result.ReviewRounds, Is.EqualTo(1));
        Assert.That(result.Code, Is.EqualTo(RevisedCoderAnswer));
        Assert.That(result.Files.Select(file => (file.Path, file.Kind)), Is.EqualTo(new[]
        {
            ("src/Orders/CancelOrder.cs", "code"), ("tests/CancelOrderTests.cs", "test")
        }));
        var revision = _model.CallsTo("coder").Last().UserMessage;
        Assert.That(revision, Does.Contain("## PREVIOUS IMPLEMENTATION\n" + CoderAnswer));
        Assert.That(revision, Does.Contain("## REVIEW FEEDBACK\nVERDICT: NEEDS_CHANGES"));
        Assert.That(_model.CallsTo("tester").Last().UserMessage, Does.Contain("## IMPLEMENTATION\n" + RevisedCoderAnswer));
    }

    [Test]
    public async Task RunAsync_NoRevisionsLeft_ReportsTheChangesAsStillNeeded()
    {
        _model.Answer("router", FeatureDecision)
            .Answer("coder", CoderAnswer)
            .Answer("reviewer", "VERDICT: NEEDS_CHANGES");

        var result = await RunAsync(new ChatRequest("x", Options: new RunOptions(MaxReviewIterations: 0)));

        Assert.That(StartedStages, Does.Not.Contain("revise"));
        Assert.That(result.Approved, Is.False);
        Assert.That(result.FinalMarkdown, Does.StartWith("## Uygulama\n\n").And.Contain("## Code Review — Değişiklik gerekli\n\n"));
    }

    [Test]
    public async Task RunAsync_UnreadableRouterDecision_RunsTheFullPipeline()
    {
        _model.Answer("router", "I am not sure.").Answer("reviewer", "VERDICT: APPROVED");

        var result = await RunAsync(new ChatRequest("x"));

        Assert.That(result.Plan, Is.EqualTo(AgentPlan.Full("Router kararı okunamadı; tam pipeline çalıştırıldı.")));
        Assert.That(StartedStages, Is.EqualTo(new[] { "translate_in", "route", "context", "analyze", "code", "test", "review" }));
    }

    [Test]
    public async Task RunAsync_ManualStages_SkipTheRouter()
    {
        _model.Answer("tester", TesterAnswer);

        var result = await RunAsync(new ChatRequest("x", Options: new RunOptions(Stages: ["TEST"])));

        Assert.That(StartedStages, Is.EqualTo(new[] { "translate_in", "context", "test" }));
        Assert.That(result.Plan.Intent, Is.EqualTo("manual"));
        // Without new code the tester works on the code from the context.
        Assert.That(_model.CallsTo("tester").Single().UserMessage, Does.Contain("## IMPLEMENTATION\n(No new code was generated."));
    }

    [Test]
    public async Task RunAsync_FullMode_SkipsTheRouter()
    {
        _model.Answer("reviewer", "VERDICT: APPROVED");

        var result = await RunAsync(new ChatRequest("x", Options: new RunOptions(Mode: "Full")));

        Assert.That(_model.CallsTo("router"), Is.Empty);
        Assert.That(result.Plan.Reason, Is.EqualTo("Tam pipeline seçildi."));
    }

    [Test]
    public async Task RunAsync_TranslatedReport_KeepsCodeBlocksVerbatim()
    {
        _model.Answer("router", AnswerDecision)
            .Answer("answerer", "Use a record for this kind of type.\n\n```csharp\npublic record Order(int Id);\n```")
            // The translator drops the code placeholder; the code must survive anyway.
            .Answer("translator-to-tr", "Bu tür bir tip için record kullanın.");

        var result = await RunAsync(new ChatRequest("x"), new PipelineOptions { TranslateOutput = true });

        Assert.That(result.FinalMarkdown, Is.EqualTo("Bu tür bir tip için record kullanın.\n\n```csharp\npublic record Order(int Id);\n```"));
        Assert.That(StartedStages.Last(), Is.EqualTo("translate_out"));
        Assert.That(result.Models["BackTranslator"], Is.EqualTo("model-backtranslator"));
    }

    [Test]
    public async Task RunAsync_EmptyTranslation_KeepsTheOriginalQuestion()
    {
        _model.Answer("translator-to-en", "");

        var result = await RunAsync(new ChatRequest("Sipariş iptali", Options: new RunOptions(Stages: ["answer"])));

        Assert.That(result.EnglishRequest, Is.EqualTo("Sipariş iptali"));
    }

    [Test]
    public async Task RunAsync_PassesNotesRecentHistoryAndModelOverridesToTheCoder()
    {
        var request = new ChatRequest(
            "x",
            ExtraContext: "Order sınıfını kullan",
            History: [new HistoryItem("first", "class A {}"), new HistoryItem("second", null), new HistoryItem("third", "class C {}")],
            Options: new RunOptions(ModelOverrides: new Dictionary<string, string> { ["Coder"] = "big-coder" }, Stages: ["code"]));

        var result = await RunAsync(request);

        var coder = _model.CallsTo("coder").Single();
        Assert.That(coder.Agent.Model, Is.EqualTo("big-coder"));
        Assert.That(result.Models["Coder"], Is.EqualTo("big-coder"));
        Assert.That(coder.UserMessage, Does.Contain("## ADDITIONAL NOTES FROM THE USER (may be in Turkish)\nOrder sınıfını kullan"));
        // PipelineOptions.MaxHistoryItems = 2
        Assert.That(coder.UserMessage, Does.Contain("Previous request: second").And.Contain("Previous request: third")
            .And.Not.Contain("Previous request: first"));
    }

    [Test]
    public async Task RunAsync_StreamsTokensAndThePlan()
    {
        _model.Answer("router", AnswerDecision).Answer("answerer", "Answer text");

        await RunAsync(new ChatRequest("x"));

        var answerTokens = _events.Where(e => e.Type == PipelineEventTypes.Token && e.Stage == PipelineStages.Answer).Select(e => e.Text);
        Assert.That(string.Concat(answerTokens), Is.EqualTo("Answer text"));
        var plan = _events.Single(e => e.Type == PipelineEventTypes.Plan);
        Assert.That(plan.Stage, Is.EqualTo(PipelineStages.Route));
        Assert.That(plan.Text, Is.EqualTo("Soru."));
    }

    [Test]
    public async Task RunAsync_UnknownProject_IsRejected()
    {
        var request = new ChatRequest("x", ProjectPath: "missing", Options: new RunOptions(Mode: RunModes.Full));

        await Assert.ThrowsAsync<InvalidRequestException>(() => RunAsync(request));
    }
}
