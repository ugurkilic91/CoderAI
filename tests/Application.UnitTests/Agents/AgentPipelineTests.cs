using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Application.UnitTests.Fakes;
using DevAgents.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Agents;

/// <summary>Kodun yalnızca önerildiği (projeye yazılmadığı) akış.</summary>
public class AgentPipelineTests
{
    private const string FeatureDecision =
        """{"intent":"feature","analyze":true,"code":true,"test":true,"review":true,"needsContext":true,"reason":"Yeni özellik."}""";
    private const string AnswerDecision = """{"intent":"question","answer":true,"needsContext":false,"reason":"Soru."}""";
    private const string CoderAnswer = "### FILE: src/Orders/CancelOrder.cs\n```csharp\nclass CancelOrder {}\n```\n\n### NOTES\n- none";
    private const string RevisedCoderAnswer = "### FILE: src/Orders/CancelOrder.cs\n```csharp\nclass CancelOrder { /* guarded */ }\n```";
    private const string TesterAnswer = "### FILE: tests/CancelOrderTests.cs\n```csharp\nclass CancelOrderTests {}\n```";

    private PipelineHarness _harness = null!;

    [SetUp]
    public void SetUp() => _harness = new PipelineHarness();

    private FakeLanguageModel Model => _harness.Model;

    [Test]
    public async Task ShouldRunOnlyTheAnswerAgentForAQuestion()
    {
        Model.Answer("router", "<think>easy</think>" + AnswerDecision)
            .Answer("answerer", "A record is an immutable reference type.");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "record nedir?" });

        _harness.StartedStages.ShouldBe(["translate_in", "route", "answer"]);
        result.Plan.Intent.ShouldBe("question");
        // Tek bölümlük rapor başlıksız yazılır.
        result.FinalMarkdown.ShouldBe("A record is an immutable reference type.");
        result.Context.Warning.ShouldBe(ProjectContext.None.Warning);
    }

    [Test]
    public async Task ShouldReviseTheCodeUntilTheReviewerApproves()
    {
        Model.Answer("router", FeatureDecision)
            .Answer("analyzer", "### Goal\nCancel orders.")
            .Answer("coder", CoderAnswer, RevisedCoderAnswer)
            .Answer("tester", TesterAnswer)
            .Answer("reviewer", "VERDICT: NEEDS_CHANGES\n### Issues\n[MAJOR] missing guard", "VERDICT: APPROVED");
        _harness.Workspace.With("Shop.csproj", "<Project />");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "Sipariş iptali ekle", ProjectPath = "Shop" });

        _harness.StartedStages.ShouldBe(["translate_in", "route", "context", "analyze", "code", "test", "review", "revise", "test", "review"]);
        result.Approved.ShouldBeTrue();
        result.ReviewRounds.ShouldBe(1);
        result.Code.ShouldBe(RevisedCoderAnswer);
        result.Files.Select(file => (file.Path, file.Kind)).ShouldBe(
            [("src/Orders/CancelOrder.cs", "code"), ("tests/CancelOrderTests.cs", "test")]);
        var revision = Model.CallsTo("coder").Last().UserMessage;
        revision.ShouldContain("## PREVIOUS IMPLEMENTATION\n" + CoderAnswer);
        revision.ShouldContain("## REVIEW FEEDBACK\nVERDICT: NEEDS_CHANGES");
        Model.CallsTo("tester").Last().UserMessage.ShouldContain("## IMPLEMENTATION\n" + RevisedCoderAnswer);
        // Öneri modunda projeye hiçbir şey yazılmaz.
        _harness.Writer.Writes.ShouldBeEmpty();
    }

    [Test]
    public async Task ShouldReportChangesAsStillNeededWhenNoRevisionsAreLeft()
    {
        Model.Answer("router", FeatureDecision).Answer("coder", CoderAnswer).Answer("reviewer", "VERDICT: NEEDS_CHANGES");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "sipariş", Options = new RunOptions(MaxReviewIterations: 0) });

        _harness.StartedStages.ShouldNotContain("revise");
        result.Approved.ShouldBeFalse();
        result.FinalMarkdown.ShouldStartWith("## Uygulama\n\n");
        result.FinalMarkdown.ShouldContain("## Code Review — Değişiklik gerekli\n\n");
    }

    [Test]
    public async Task ShouldRunTheFullPipelineWhenTheRouterDecisionIsUnreadable()
    {
        Model.Answer("router", "I am not sure.").Answer("reviewer", "VERDICT: APPROVED");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "şunu yap" });

        result.Plan.ShouldBe(AgentPlan.Full("Router kararı okunamadı; tam pipeline çalıştırıldı."));
        _harness.StartedStages.ShouldBe(["translate_in", "route", "context", "analyze", "code", "test", "review"]);
    }

    [Test]
    public async Task ShouldSkipTheRouterForManualStages()
    {
        Model.Answer("tester", TesterAnswer);

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "testleri yaz", Options = new RunOptions(Stages: ["TEST"]) });

        _harness.StartedStages.ShouldBe(["translate_in", "context", "test"]);
        result.Plan.Intent.ShouldBe("manual");
        // Yeni kod yoksa test ajanı bağlamdaki kod üzerinde çalışır.
        Model.CallsTo("tester").Single().UserMessage.ShouldContain("## IMPLEMENTATION\n(No new code was generated.");
    }

    [Test]
    public async Task ShouldSkipTheRouterInFullMode()
    {
        Model.Answer("reviewer", "VERDICT: APPROVED");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "x", Options = new RunOptions(Mode: "Full") });

        Model.CallsTo("router").ShouldBeEmpty();
        result.Plan.Reason.ShouldBe("Tam pipeline seçildi.");
    }

    [Test]
    public async Task ShouldKeepCodeBlocksVerbatimInTheTranslatedReport()
    {
        Model.Answer("router", AnswerDecision)
            .Answer("answerer", "Use a record for this kind of type.\n\n```csharp\npublic record Order(int Id);\n```")
            // Çevirmen kod yer tutucusunu kaybediyor; kod yine de kaybolmamalı.
            .Answer("translator-to-tr", "Bu tür bir tip için record kullanın.");
        _harness.PipelineOptions.TranslateOutput = true;

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "record nedir?" });

        result.FinalMarkdown.ShouldBe("Bu tür bir tip için record kullanın.\n\n```csharp\npublic record Order(int Id);\n```");
        _harness.StartedStages.Last().ShouldBe("translate_out");
        result.Models["BackTranslator"].ShouldBe("model-backtranslator");
    }

    [Test]
    public async Task ShouldKeepTheOriginalQuestionWhenTheTranslationIsEmpty()
    {
        Model.Answer("translator-to-en", "");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "Sipariş iptali", Options = new RunOptions(Stages: ["answer"]) });

        result.EnglishRequest.ShouldBe("Sipariş iptali");
    }

    [Test]
    public async Task ShouldNotTranslateARequestThatIsAlreadyEnglish()
    {
        Model.Answer("router", AnswerDecision).Answer("answerer", "Answer");

        var result = await _harness.RunAsync(new RunAgentPipelineCommand { Question = "Explain the order cancel endpoint" });

        Model.CallsTo("translator-to-en").ShouldBeEmpty();
        result.EnglishRequest.ShouldBe("Explain the order cancel endpoint");
        _harness.StartedStages.First().ShouldBe("route");
    }

    [Test]
    public async Task ShouldTranslateEveryRequestWhenLanguageDetectionIsOff()
    {
        Model.Answer("router", AnswerDecision);
        _harness.PipelineOptions.DetectInputLanguage = false;

        await _harness.RunAsync(new RunAgentPipelineCommand { Question = "Explain the order cancel endpoint" });

        Model.CallsTo("translator-to-en").Count().ShouldBe(1);
    }

    [Test]
    public async Task ShouldPassNotesRecentHistoryAndModelOverridesToTheCoder()
    {
        var request = new RunAgentPipelineCommand
        {
            Question = "doğrulama ekle",
            ExtraContext = "Order sınıfını kullan",
            History = [new HistoryItem("first", "class A {}"), new HistoryItem("second", null), new HistoryItem("third", "class C {}", "1. Which fields?")],
            Options = new RunOptions(ModelOverrides: new Dictionary<string, string> { ["Coder"] = "big-coder" }, Stages: ["code"])
        };

        var result = await _harness.RunAsync(request);

        var coder = Model.CallsTo("coder").Single();
        coder.Agent.Model.ShouldBe("big-coder");
        result.Models["Coder"].ShouldBe("big-coder");
        coder.UserMessage.ShouldContain("## ADDITIONAL NOTES FROM THE USER (may be in Turkish)\nOrder sınıfını kullan");
        // PipelineOptions.MaxHistoryItems = 2: yalnızca son iki tur; ajanın sorduğu sorular da geçmişe girer.
        coder.UserMessage.ShouldContain("Previous request: second");
        coder.UserMessage.ShouldContain("Previous request: third");
        coder.UserMessage.ShouldContain("Questions you asked the user (the new request may answer them):\n1. Which fields?");
        coder.UserMessage.ShouldNotContain("Previous request: first");
    }

    [Test]
    public async Task ShouldStreamTokensAndThePlan()
    {
        Model.Answer("router", AnswerDecision).Answer("answerer", "Answer text");

        await _harness.RunAsync(new RunAgentPipelineCommand { Question = "x" });

        string.Concat(_harness.Events.Where(e => e is { Type: PipelineEventTypes.Token, Stage: PipelineStages.Answer }).Select(e => e.Text))
            .ShouldBe("Answer text");
        var plan = _harness.Events.Single(e => e.Type == PipelineEventTypes.Plan);
        plan.Stage.ShouldBe(PipelineStages.Route);
        plan.Text.ShouldBe("Soru.");
    }

    [Test]
    public async Task ShouldRejectAnUnknownProject()
    {
        var request = new RunAgentPipelineCommand { Question = "x", ProjectPath = "missing", Options = new RunOptions(Mode: RunModes.Full) };

        await Should.ThrowAsync<ProjectPathException>(() => _harness.RunAsync(request));
    }

    [Test]
    public async Task ShouldGiveTheArchitectureRulesAndExamplesToTheAgents()
    {
        Model.Answer("router", FeatureDecision).Answer("reviewer", "VERDICT: APPROVED");
        _harness.Workspace.With("src/Domain/Entities/TodoItem.cs", "namespace Shop.Domain.Entities; public class TodoItem {}");

        await _harness.RunAsync(new RunAgentPipelineCommand { Question = "hobi ekle", ProjectPath = "Shop", Architecture = "clean-architecture" });

        var analysis = Model.CallsTo("analyzer").Single().UserMessage;
        analysis.ShouldContain("## ARCHITECTURE: Clean Architecture\nFollow these rules exactly; they override general habits.\n\nPut entities in src/Domain/Entities.");
        analysis.ShouldContain("<file path=\"src/Domain/Entities/TodoItem.cs\">\nnamespace Shop.Domain.Entities; public class TodoItem {}\n</file>");
        // Ek örnekteki şablon adı proje adıyla değiştirilir.
        analysis.ShouldContain("<file path=\"tests/Unit/ShopTests.cs\">\nnamespace Shop.Tests;\n</file>");
        // Örnek olarak verilen dosya proje bağlamına ikinci kez eklenmez.
        analysis.Split("src/Domain/Entities/TodoItem.cs\">").Length.ShouldBe(2);
    }
}
