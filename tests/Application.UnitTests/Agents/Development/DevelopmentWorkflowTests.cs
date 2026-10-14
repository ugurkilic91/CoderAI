using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.UnitTests.Fakes;
using DevAgents.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Agents.Development;

/// <summary>Kodun projeye yazılıp derlendiği ve test edildiği akış (ApplyChanges).</summary>
public class DevelopmentWorkflowTests
{
    private const string FeatureDecision =
        """{"intent":"feature","analyze":true,"code":true,"test":true,"review":false,"needsContext":true,"reason":"Yeni özellik."}""";
    private const string CoderAnswer =
        "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby {}\n```\n\n### NOTES\n- Hobiler için tablo EnsureCreated ile oluşur.";
    private const string TesterAnswer = "### FILE: tests/Unit/HobbyTests.cs\n```csharp\npublic class HobbyTests {}\n```";

    private PipelineHarness _harness = null!;

    [SetUp]
    public void SetUp()
    {
        _harness = new PipelineHarness();
        _harness.Model.Answer("router", FeatureDecision)
            .Answer("analyzer", "### Goal\nStore hobbies.\n\n### Questions for the user\nNone")
            .Answer("coder", CoderAnswer)
            .Answer("tester", TesterAnswer);
        _harness.Workspace.With("Shop.slnx", "<Solution />");
    }

    private static RunAgentPipelineCommand Apply(string question = "Hobilerimi kaydeden kodu geliştir") =>
        new() { Question = question, ProjectPath = "Shop", Architecture = "clean-architecture", ApplyChanges = true };

    [Test]
    public async Task ShouldWriteBuildAndTestTheGeneratedCode()
    {
        _harness.Sandbox.Test(FakeBuildSandbox.Passed(3));

        var result = await _harness.RunAsync(Apply());

        _harness.StartedStages.ShouldBe(
        [
            "translate_in", "route", "context", "analyze", "code", "syntax", "write", "build",
            "test", "syntax", "write", "build", "test_run"
        ]);
        // Biri değişikliklerden, biri testlerden önce (testler derlenemezse yalnızca test adımı geri alınsın diye).
        _harness.Tracker.Snapshots.ShouldBe(2);
        _harness.Workspace.Files["src/Domain/Entities/Hobby.cs"].ShouldBe("public class Hobby {}");
        _harness.Workspace.Files["tests/Unit/HobbyTests.cs"].ShouldBe("public class HobbyTests {}");

        var development = result.Development.ShouldNotBeNull();
        development.ProjectPath.ShouldBe("/projects/Shop");
        development.Snapshot.ShouldBe(FakeChangeTracker.Snapshot);
        development.Build!.Succeeded.ShouldBeTrue();
        development.Tests!.Passed.ShouldBe(3);
        development.Problem.ShouldBeNull();
        development.Changes.Select(change => change.Path).ShouldBe(["src/Domain/Entities/Hobby.cs", "tests/Unit/HobbyTests.cs"]);
        result.Files.Select(file => (file.Path, file.Kind)).ShouldBe(
            [("src/Domain/Entities/Hobby.cs", GeneratedFileKind.Code), ("tests/Unit/HobbyTests.cs", GeneratedFileKind.Test)]);

        // Rapor: kodla yazılan Türkçe özet + analiz + kodlama ajanının notları (kodun kendisi değil).
        result.FinalMarkdown.ShouldStartWith("## Geliştirme özeti");
        result.FinalMarkdown.ShouldContain("**Build:** ✅ başarılı");
        result.FinalMarkdown.ShouldContain("**Testler:** ✅ 3/3 başarılı");
        result.FinalMarkdown.ShouldContain("## Notlar\n\n- Hobiler için tablo EnsureCreated ile oluşur.");
        result.FinalMarkdown.ShouldNotContain("## Uygulama");
        result.FinalMarkdown.ShouldNotContain("Questions for the user");
    }

    [Test]
    public async Task ShouldGiveBuildErrorsToTheFixerAndRebuild()
    {
        var error = new CompilerDiagnostic("src/Domain/Entities/Hobby.cs", 1, 14, "CS0246", "The type 'Foo' could not be found");
        _harness.Sandbox.Build(new BuildResult(false, [error], "error", 1), FakeBuildSandbox.Success);
        _harness.Model.Answer("fixer", "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { }\n```");

        var result = await _harness.RunAsync(Apply());

        var fix = _harness.Model.CallsTo("fixer").Single();
        fix.Agent.Name.ShouldBe("Coder");
        fix.UserMessage.ShouldContain("## BUILD ERRORS\n- src/Domain/Entities/Hobby.cs(1,14): CS0246 The type 'Foo' could not be found");
        fix.UserMessage.ShouldContain("<file path=\"src/Domain/Entities/Hobby.cs\">\npublic class Hobby {}\n</file>");
        fix.UserMessage.ShouldContain("## ARCHITECTURE: Clean Architecture");
        _harness.Workspace.Files["src/Domain/Entities/Hobby.cs"].ShouldBe("public class Hobby { }");
        result.Development!.Build!.Succeeded.ShouldBeTrue();
        result.Development.BuildAttempts.ShouldBe(3);
        result.FinalMarkdown.ShouldContain("**Build:** ✅ başarılı (3 derleme; 1 düzeltme turu)");
    }

    [Test]
    public async Task ShouldStopFixingWhenTheAttemptsAreUsedUp()
    {
        var error = new CompilerDiagnostic("src/Domain/Entities/Hobby.cs", 1, 1, "CS1000", "broken");
        _harness.Sandbox.Build(new BuildResult(false, [error], "error", 1));
        _harness.Model.Answer("fixer",
            "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { /* still broken */ }\n```",
            "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { /* broken again */ }\n```");
        _harness.DevelopmentOptions.MaxBuildFixAttempts = 2;

        var result = await _harness.RunAsync(Apply());

        _harness.Model.CallsTo("fixer").Count().ShouldBe(2);
        _harness.Sandbox.Builds.ShouldBe(3);
        _harness.Model.CallsTo("tester").ShouldBeEmpty();
        result.Development!.Build!.Succeeded.ShouldBeFalse();
        result.Development.Problem.ShouldBe("Kod, düzeltme turlarına rağmen derlenemedi; testler çalıştırılmadı.");
        result.FinalMarkdown.ShouldContain("**Build:** ❌ 1 hata (3 derleme; 2 düzeltme turu)");
    }

    [Test]
    public async Task ShouldStopFixingWhenTheFixerChangesNothing()
    {
        var error = new CompilerDiagnostic("src/Domain/Entities/Hobby.cs", 1, 1, "CS1000", "broken");
        _harness.Sandbox.Build(new BuildResult(false, [error], "error", 1));
        // Düzeltme ajanı dosyayı diskteki hâliyle aynen geri veriyor: aynı derlemeyi tekrarlamanın anlamı yok.
        _harness.Model.Answer("fixer", CoderAnswer);

        var result = await _harness.RunAsync(Apply());

        _harness.Model.CallsTo("fixer").Count().ShouldBe(1);
        _harness.Sandbox.Builds.ShouldBe(1);
        result.Development!.Build!.Succeeded.ShouldBeFalse();
    }

    [Test]
    public async Task ShouldAddMissingUsingsWithoutTheFixerAgent()
    {
        var error = new CompilerDiagnostic("src/Domain/Entities/Hobby.cs", 1, 14, "CS0246", "The type or namespace name 'BaseEntity' could not be found");
        _harness.Sandbox.Build(new BuildResult(false, [error], "error", 1), FakeBuildSandbox.Success);
        _harness.UsingFixer.Resolve("src/Domain/Entities/Hobby.cs", "Shop.Domain.Common");

        var result = await _harness.RunAsync(Apply());

        // Eksik using model kullanmadan eklendi ve proje yeniden derlendi; düzeltme ajanına gerek kalmadı.
        _harness.Workspace.Files["src/Domain/Entities/Hobby.cs"].ShouldBe("using Shop.Domain.Common;\npublic class Hobby {}");
        _harness.Model.CallsTo("fixer").ShouldBeEmpty();
        _harness.StartedStages.ShouldContain("usings");
        _harness.Events.Single(e => e.Type == PipelineEventTypes.StageEnd && e.Stage == "usings").Text.ShouldBe("1 using eklendi");
        result.Development!.Build!.Succeeded.ShouldBeTrue();
        result.Development.BuildAttempts.ShouldBe(3);
        result.Development.UsingsAdded.ShouldBe(1);
        result.FinalMarkdown.ShouldContain("**Build:** ✅ başarılı (3 derleme; 1 eksik using otomatik eklendi)");
    }

    [Test]
    public async Task ShouldRestoreMembersTheCoderDroppedWithoutTheFixerAgent()
    {
        // Kodlama ajanı mevcut arayüzü değiştirirken TodoLists üyesini düşürdü; şablonun kendi kodu onu kullanıyor.
        const string contextPath = "src/Application/Common/Interfaces/IAppDb.cs";
        _harness.Workspace.With(contextPath, "public interface IAppDb\n{\n    int TodoLists { get; }\n}\n");
        _harness.Model.Answer("coder",
            $"### FILE: {contextPath}\n```csharp\npublic interface IAppDb\n{{\n    int Hobbies {{ get; }}\n}}\n```\n\n### NOTES\n- none");
        var error = new CompilerDiagnostic("src/Application/TodoLists/GetTodos.cs", 12, 30, "CS1061",
            "'IAppDb' does not contain a definition for 'TodoLists' and no accessible extension method 'TodoLists' accepting a first argument of type 'IAppDb' could be found");
        _harness.Sandbox.Build(new BuildResult(false, [error], "error", 1), FakeBuildSandbox.Success);

        var result = await _harness.RunAsync(Apply());

        _harness.Workspace.Files[contextPath].ShouldBe("public interface IAppDb\n{\n    int Hobbies { get; }\n    int TodoLists { get; }\n}");
        _harness.Model.CallsTo("fixer").ShouldBeEmpty();
        _harness.StartedStages.ShouldContain("restore");
        result.Development!.MembersRestored.ShouldBe(1);
        result.FinalMarkdown.ShouldContain("**Build:** ✅ başarılı (3 derleme; silinen 1 üye otomatik geri eklendi)");
    }

    [Test]
    public async Task ShouldLeaveTestsToTheTestAgent()
    {
        // Kodlama ajanı kuralı çiğneyip test de yazdı: o test projeye alınmaz, testleri test ajanı yazar.
        _harness.Model.Answer("coder", CoderAnswer.Replace("### NOTES",
            "### FILE: tests/Unit/CoderTests.cs\n```csharp\npublic class CoderTests : TestBase {}\n```\n\n### NOTES", StringComparison.Ordinal));

        var result = await _harness.RunAsync(Apply());

        _harness.Workspace.Files.ShouldNotContainKey("tests/Unit/CoderTests.cs");
        _harness.Workspace.Files.ShouldContainKey("tests/Unit/HobbyTests.cs");
        _harness.Writer.Writes[0].ShouldBe(["src/Domain/Entities/Hobby.cs"]);
        result.FinalMarkdown.ShouldContain("test agent");
    }

    [Test]
    public async Task ShouldGiveTheFixerTheArchitectureExamplesOfTheFailingArea()
    {
        var error = new CompilerDiagnostic("tests/Unit/HobbyTests.cs", 1, 27, "CS0246", "The type or namespace name 'TestBase' could not be found");
        _harness.Sandbox.Build(FakeBuildSandbox.Success, new BuildResult(false, [error], "error", 1), FakeBuildSandbox.Success);
        _harness.Model.Answer("fixer", "### FILE: tests/Unit/HobbyTests.cs\n```csharp\npublic class HobbyTests { }\n```");

        await _harness.RunAsync(Apply());

        // Profilin test örneği (şablon adı proje adıyla değiştirilmiş) düzeltme mesajında; Domain örneği değil.
        var message = _harness.Model.CallsTo("fixer").Single().UserMessage;
        message.ShouldContain("## REFERENCE FILES OF THE ARCHITECTURE");
        message.ShouldContain("<file path=\"tests/Unit/ShopTests.cs\">\nnamespace Shop.Tests;\n</file>");
    }

    [Test]
    public async Task ShouldRollBackOnlyTheTestStepWhenTheTestsDoNotCompile()
    {
        var error = new CompilerDiagnostic("tests/Unit/HobbyTests.cs", 1, 27, "CS0246", "The type or namespace name 'TestBase' could not be found");
        _harness.Sandbox.Build(FakeBuildSandbox.Success, new BuildResult(false, [error], "error", 1), FakeBuildSandbox.Success);
        // Düzeltme ajanı testi değiştiremiyor: aynı içeriği geri veriyor.
        _harness.Model.Answer("fixer", TesterAnswer);

        var result = await _harness.RunAsync(Apply());

        // Derlenmeyen test projeden çıkarıldı, derlenen kod duruyor ve proje yeniden derlendi.
        _harness.Workspace.Files.ShouldNotContainKey("tests/Unit/HobbyTests.cs");
        _harness.Workspace.Files["src/Domain/Entities/Hobby.cs"].ShouldBe("public class Hobby {}");
        var development = result.Development.ShouldNotBeNull();
        development.Build!.Succeeded.ShouldBeTrue();
        development.BuildAttempts.ShouldBe(3);
        development.Tests.ShouldBeNull();
        development.Problem.ShouldBe(
            "Test ajanının yazdığı testler düzeltme turlarına rağmen derlenemedi; projenin derlenebilir kalması için test adımı geri alındı.");
        development.Changes.Select(change => change.Path).ShouldBe(["src/Domain/Entities/Hobby.cs"]);
        result.Files.Select(file => file.Path).ShouldBe(["src/Domain/Entities/Hobby.cs"]);
    }

    [Test]
    public async Task ShouldGiveFailingTestsToTheFixerAndRunThemAgain()
    {
        var failure = new TestFailure("Shop.Tests.HobbyTests.ShouldStoreName", "Expected \"Chess\" but was null", "at HobbyTests.cs:line 12");
        _harness.Sandbox.Test(new TestRunResult(2, 1, 1, 0, [failure], "Failed!", 1), FakeBuildSandbox.Passed(2));
        _harness.Model.Answer("fixer", "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { public string? Name { get; set; } }\n```");

        var result = await _harness.RunAsync(Apply());

        _harness.Model.CallsTo("fixer").Single().UserMessage.ShouldContain(
            "## FAILING TESTS (1 of 2)\n- Shop.Tests.HobbyTests.ShouldStoreName\n  Expected \"Chess\" but was null\n    at HobbyTests.cs:line 12");
        result.Development!.Tests!.AllPassed().ShouldBeTrue();
        result.Development.TestAttempts.ShouldBe(2);
        result.FinalMarkdown.ShouldContain("**Testler:** ✅ 2/2 başarılı (2. çalıştırmada)");
    }

    [Test]
    public async Task ShouldLetTheFixerRepairSyntaxErrorsBeforeWriting()
    {
        _harness.Model.Answer("coder", "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { SYNTAX_ERROR }\n```")
            .Answer("fixer", "### FILE: src/Domain/Entities/Hobby.cs\n```csharp\npublic class Hobby { }\n```");

        await _harness.RunAsync(Apply());

        _harness.Model.CallsTo("fixer").First().UserMessage.ShouldContain("## SYNTAX ERRORS\n- src/Domain/Entities/Hobby.cs(1,1): CS1002 ; expected");
        // Hatalı sürüm projeye hiç yazılmadı.
        _harness.Writer.Writes[0].ShouldBe(["src/Domain/Entities/Hobby.cs"]);
        _harness.Workspace.Files["src/Domain/Entities/Hobby.cs"].ShouldBe("public class Hobby { }");
    }

    [Test]
    public async Task ShouldAskTheUserBeforeCodingWhenInformationIsMissing()
    {
        _harness.Model.Answer("analyzer", "### Goal\nStore hobbies.\n\n### Questions for the user\n1. Which fields should a hobby have?");

        var result = await _harness.RunAsync(Apply() with { Options = new RunOptions(AskQuestions: true) });

        _harness.Model.CallsTo("coder").ShouldBeEmpty();
        _harness.Writer.Writes.ShouldBeEmpty();
        result.Questions.ShouldBe("1. Which fields should a hobby have?");
        result.Development.ShouldBeNull();
        result.FinalMarkdown.ShouldStartWith("## Sorular\n\n1. Which fields should a hobby have?");
        result.FinalMarkdown.ShouldContain("## Analiz\n\n### Goal\nStore hobbies.");
    }

    [Test]
    public async Task ShouldNotWriteAnythingWhenTheCoderProducesNoFiles()
    {
        _harness.Model.Answer("coder", "Sorry, I cannot help with that.");

        var result = await _harness.RunAsync(Apply());

        _harness.Writer.Writes.ShouldBeEmpty();
        _harness.Tracker.Snapshots.ShouldBe(0);
        result.Development!.Problem.ShouldStartWith("Kodlama ajanı ayrıştırılabilir bir dosya üretmedi");
    }

    [Test]
    public async Task ShouldCreateANewProjectFromTheArchitectureTemplate()
    {
        var request = Apply() with { ProjectPath = null, NewProjectName = "HobbyTracker" };

        var result = await _harness.RunAsync(request);

        _harness.Sandbox.CreatedProjects.ShouldBe(["HobbyTracker"]);
        _harness.StartedStages.ShouldContain("scaffold");
        result.Development!.ProjectPath.ShouldBe("/projects/HobbyTracker");
        result.Development.ProjectCreated.ShouldBeTrue();
        result.FinalMarkdown.ShouldContain("**Proje:** `/projects/HobbyTracker` (bu istekte şablondan oluşturuldu)");
    }
}
