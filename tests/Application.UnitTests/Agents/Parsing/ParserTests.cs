using DevAgents.Application.Agents.Development;
using DevAgents.Application.Agents.Parsing;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Agents.Parsing;

public class RouterDecisionParserTests
{
    [Test]
    public void ShouldReadJsonWrappedInProseAndFences()
    {
        var plan = RouterDecisionParser.Parse("Sure!\n```json\n{\"intent\":\"bug_fix\",\"code\":true,\"test\":true,\"reason\":\"Hata.\"}\n```");

        plan.ShouldBe(new AgentPlan("bug_fix", false, false, true, true, false, NeedsContext: true, "Hata."));
    }

    [Test]
    public void ShouldKeepAnExplicitNoContextAndDefaultTheIntent()
    {
        var plan = RouterDecisionParser.Parse("""{"answer":true,"needsContext":false}""");

        plan.ShouldNotBeNull();
        plan.NeedsContext.ShouldBeFalse();
        plan.Intent.ShouldBe("custom");
    }

    [Test]
    public void ShouldApplyThePlanningRules()
    {
        var plan = RouterDecisionParser.Parse("""{"intent":"question","answer":true,"code":true}""");

        plan.ShouldNotBeNull();
        plan.Answer.ShouldBeFalse();
        plan.Code.ShouldBeTrue();
    }

    [Test]
    public void ShouldEnableAnAgentOnlyForJsonTrue()
    {
        var plan = RouterDecisionParser.Parse("""{"code":"yes","test":1,"review":true}""");

        plan.ShouldNotBeNull();
        plan.Code.ShouldBeFalse();
        plan.Test.ShouldBeFalse();
        plan.Review.ShouldBeTrue();
    }

    [TestCase("no json here")]
    [TestCase("{\"intent\": \"feature\", \"code\": tru")]
    [TestCase("{ not json }")]
    public void ShouldReturnNullForUnreadableOutput(string output)
    {
        RouterDecisionParser.Parse(output).ShouldBeNull();
    }
}

public class ReviewVerdictParserTests
{
    [TestCase("VERDICT: APPROVED\n### Issues\nNone", true)]
    [TestCase("verdict:approved", true)]
    [TestCase("VERDICT: NEEDS_CHANGES\n### Issues\n[MAJOR] ...", false)]
    [TestCase("VERDICT: needs changes", false)]
    [TestCase("VERDICT: APPROVED, but VERDICT: NEEDS_CHANGES after all", false)]
    [TestCase("Looks good to me.", false)]
    public void ShouldReadTheVerdict(string review, bool expected)
    {
        ReviewVerdictParser.IsApproved(review).ShouldBe(expected);
    }

    [Test]
    public void ShouldIgnoreAVerdictAfterTheFirst400Characters()
    {
        ReviewVerdictParser.IsApproved(new string('x', 400) + "VERDICT: APPROVED").ShouldBeFalse();
    }
}

public class GeneratedFileParserTests
{
    [Test]
    public void ShouldReadFileBlocksAndSkipUnsafePaths()
    {
        const string markdown =
            "### FILE: src/A.cs\n```csharp\nclass A {}\n```\n\n" +
            "### FILE: ../evil.cs\n```csharp\nbad\n```\n\n" +
            "### FILE: `./src/B.cs`\r\n```csharp\r\nclass B {}\r\n```\n\n" +
            "### NOTES\n- none";

        GeneratedFileParser.Parse(markdown, GeneratedFileKind.Code).ShouldBe(
        [
            new GeneratedFile("src/A.cs", "class A {}", GeneratedFileKind.Code),
            new GeneratedFile("src/B.cs", "class B {}", GeneratedFileKind.Code)
        ]);
    }

    [Test]
    public void ShouldNotTreatACodeBlockWithoutAFileHeaderAsAFile()
    {
        GeneratedFileParser.Parse("Example:\n```csharp\nclass A {}\n```", GeneratedFileKind.Test).ShouldBeEmpty();
    }
}

public class MissingMembersTests
{
    [Test]
    public void ShouldReadMissingMembersOnlyFromCodeThatExistedBefore()
    {
        CompilerDiagnostic[] errors =
        [
            new("src/Application/TodoLists/GetTodos.cs", 43, 36, "CS1061",
                "'IApplicationDbContext' does not contain a definition for 'TodoLists' and no accessible extension method 'TodoLists' accepting a first argument of type 'IApplicationDbContext' could be found"),
            new("src/Infrastructure/Data/ApplicationDbContext.cs", 12, 14, "CS0535",
                "'ApplicationDbContext' does not implement interface member 'IApplicationDbContext.TodoItems'"),
            // Ajanın bu görevde yazdığı dosyadaki "üye yok" hatası: niyet belirsiz, geri ekleme yapılmaz.
            new("src/Application/Hobbies/GetHobbies.cs", 5, 5, "CS1061", "'Hobby' does not contain a definition for 'Title'"),
            new("src/Web/Endpoints/Hobbies.cs", 3, 3, "CS0246", "The type or namespace name 'Ok<>' could not be found")
        ];

        var missing = MissingMembers.From(errors, path => path.Contains("Hobb", StringComparison.Ordinal) || path.EndsWith("ApplicationDbContext.cs", StringComparison.Ordinal));

        missing.ShouldBe([new MissingMember("IApplicationDbContext", "TodoLists"), new MissingMember("ApplicationDbContext", "TodoItems")]);
    }
}
