using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Application.UnitTests.Fakes;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Context;

public class ProjectContextBuilderTests
{
    private static ProjectContextBuilder CreateBuilder(FakeWorkspace workspace, WorkspaceOptions? options = null) =>
        new(workspace, new StaticOptionsMonitor<WorkspaceOptions>(options ?? new WorkspaceOptions()));

    [Test]
    public void ShouldReturnTheEmptyContextWithoutProjectOrFiles()
    {
        var context = CreateBuilder(new FakeWorkspace()).Build(null, null, "anything");

        context.ShouldBeSameAs(ProjectContext.None);
        context.Text.ShouldEndWith("### RELEVANT PROJECT FILES\n(none)");
    }

    [Test]
    public void ShouldIncludePinnedFilesFirstEvenOverBudget()
    {
        var builder = CreateBuilder(new FakeWorkspace().With("Program.cs", "var app = 1;"), new WorkspaceOptions { MaxContextChars = 10 });

        var context = builder.Build("Shop", [new InlineFile("notes.txt", new string('x', 500), Pinned: true)], "");

        context.Included.ShouldBe(["notes.txt"]);
        context.TotalFiles.ShouldBe(2);
    }

    [Test]
    public void ShouldOnlyAcceptSafeSourceFilesThatAreNotPinned()
    {
        InlineFile[] files =
        [
            new("src/Alpha.cs", "class Alpha {}"), new(".env", "KEY=1"), new("notes.txt", "x"),
            new("../Beta.cs", "class Beta {}"), new("cert.pem", "-----", Pinned: true)
        ];

        var context = CreateBuilder(new FakeWorkspace()).Build(null, files, "Alpha");

        context.TotalFiles.ShouldBe(2);
        context.Included.ShouldBe(["cert.pem", "src/Alpha.cs"]);
        context.Overview.ShouldStartWith("Project: (inline files) (2 indexed files)");
    }

    [Test]
    public void ShouldRankFilesByRelevanceToTheQuery()
    {
        var workspace = new FakeWorkspace()
            .With("Services/CustomerService.cs", "class CustomerService { }")
            .With("Domain/Order.cs", "class Order { }")
            .With("Services/OrderService.cs", "class OrderService { void Cancel() {} }");

        var context = CreateBuilder(workspace).Build("Shop", null, "Cancel the order in OrderService");

        context.Included.ShouldBe(["Services/OrderService.cs", "Domain/Order.cs"]);
    }

    [Test]
    public void ShouldDescribeProjectsLayersAndTheFileTree()
    {
        var workspace = new FakeWorkspace()
            .With("Shop.csproj", """
                <Project>
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MediatR" Version="12.0.0" />
                    <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
                  </ItemGroup>
                </Project>
                """)
            .With("Controllers/OrdersController.cs", "class OrdersController {}");

        var context = CreateBuilder(workspace).Build("Shop", null, "");

        context.Overview.ShouldStartWith("Project: Shop (2 indexed files)");
        context.Overview.ShouldContain("- Shop.csproj: net10.0; packages: [MediatR]; references: [Shop.Domain]");
        context.Overview.ShouldContain("Folder names suggest these layers/patterns: Controllers");
        context.Overview.ShouldEndWith("  Controllers/OrdersController.cs");
    }

    [Test]
    public void ShouldWarnWhenTheFileLimitIsReached()
    {
        var workspace = new FakeWorkspace().With("A.cs", "class A {}").With("B.cs", "class B {}");

        var context = CreateBuilder(workspace, new WorkspaceOptions { MaxFiles = 2 }).Build("Shop", null, "");

        context.Warning.ShouldBe("Dosya sayısı sınırına ulaşıldı (2); bazı dosyalar indekslenmedi.");
    }

    [Test]
    public void ShouldRejectAnUnknownProject()
    {
        Should.Throw<ProjectPathException>(() => CreateBuilder(new FakeWorkspace()).Build("missing", null, ""));
    }

    [Test]
    public void ShouldSplitIdentifiersAndDropStopWords()
    {
        QueryTokenizer.Tokenize("Add validation to CreateTodoItemCommand")
            .ShouldBe(["validation", "createtodoitemcommand", "todo", "item", "command"]);
    }
}
