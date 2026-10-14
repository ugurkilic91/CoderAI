using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.CodeAnalysis;
using DevAgents.Infrastructure.Sandbox;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.Sandbox;

public class MsBuildOutputParserTests
{
    [Test]
    public void ShouldReadCompilerAndProjectErrorsOnceWithRelativePaths()
    {
        const string output = """
            /workspace/src/Application/Hobbies/CreateHobby.cs(12,17): error CS0246: The type or namespace name 'Hobby' could not be found (are you missing a using directive or an assembly reference?) [/workspace/src/Application/Application.csproj]
            /workspace/src/Web/Web.csproj : error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org [/workspace/App.slnx]
            /workspace/src/Domain/Entities/Hobby.cs(3,5): warning CS8618: Non-nullable property 'Name' must contain a non-null value [/workspace/src/Domain/Domain.csproj]
            Build FAILED.
            /workspace/src/Application/Hobbies/CreateHobby.cs(12,17): error CS0246: The type or namespace name 'Hobby' could not be found (are you missing a using directive or an assembly reference?) [/workspace/src/Application/Application.csproj]
            """;

        MsBuildOutputParser.ParseErrors(output, "/workspace").ShouldBe(
        [
            new CompilerDiagnostic("src/Application/Hobbies/CreateHobby.cs", 12, 17, "CS0246",
                "The type or namespace name 'Hobby' could not be found (are you missing a using directive or an assembly reference?)"),
            new CompilerDiagnostic("src/Web/Web.csproj", 0, 0, "NU1101",
                "Unable to find package Foo. No packages exist with this id in source(s): nuget.org")
        ]);
    }
}

public class TestResultParserTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("devagents-trx-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void ShouldReadCountsAndFailuresFromTrxFiles()
    {
        var path = Path.Combine(_directory, "result0.trx");
        File.WriteAllText(path, """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Shop.Tests.ShouldCreate" outcome="Passed" />
                <UnitTestResult testName="Shop.Tests.ShouldValidate" outcome="Failed">
                  <Output><ErrorInfo><Message>Expected True but was False</Message><StackTrace>at Shop.Tests.ShouldValidate() line 20</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testName="Shop.Tests.Later" outcome="NotExecuted" />
              </Results>
            </TestRun>
            """);

        var result = TestResultParser.FromTrxFiles([path]).ShouldNotBeNull();

        (result.Total, result.Passed, result.Failed, result.Skipped).ShouldBe((3, 1, 1, 1));
        result.Failures.ShouldBe([new TestFailure("Shop.Tests.ShouldValidate", "Expected True but was False", "at Shop.Tests.ShouldValidate() line 20")]);
        result.AllPassed().ShouldBeFalse();
    }

    [Test]
    public void ShouldReturnNullWithoutTrxFiles()
    {
        TestResultParser.FromTrxFiles([]).ShouldBeNull();
    }

    [Test]
    public void ShouldFallBackToTheConsoleSummary()
    {
        const string output = """
              Failed Shop.Tests.ShouldValidate [12 ms]
              Error Message:
               Expected True but was False
              Stack Trace:
                 at Shop.Tests.ShouldValidate()
            Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5, Duration: 30 ms - Shop.Tests.dll (net10.0)
            Passed!  - Failed:     0, Passed:     6, Skipped:     1, Total:     7, Duration: 9 ms - Shop.Domain.Tests.dll (net10.0)
            """;

        var result = TestResultParser.FromConsole(output);

        (result.Total, result.Passed, result.Failed, result.Skipped).ShouldBe((12, 10, 1, 1));
        result.Failures.Single().Name.ShouldBe("Shop.Tests.ShouldValidate");
        result.Failures.Single().Message.ShouldBe("Expected True but was False");
    }
}

public class RoslynSyntaxCheckerTests
{
    private readonly RoslynSyntaxChecker _checker = new();

    [Test]
    public void ShouldAcceptValidCode()
    {
        _checker.Check([new GeneratedFile("src/A.cs", "namespace A;\npublic record Hobby(string Name);", GeneratedFileKind.Code)]).ShouldBeEmpty();
    }

    [Test]
    public void ShouldReportSyntaxErrorsWithTheirPosition()
    {
        var errors = _checker.Check(
        [
            new GeneratedFile("src/Broken.cs", "public class Broken\n{\n    public void Run() { var x = 1 }\n", GeneratedFileKind.Code),
            new GeneratedFile("appsettings.json", "{ not c# }", GeneratedFileKind.Code)
        ]);

        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(error => error.File == "src/Broken.cs");
        errors[0].Line.ShouldBe(3);
        errors[0].Code.ShouldBe("CS1002");
    }
}
