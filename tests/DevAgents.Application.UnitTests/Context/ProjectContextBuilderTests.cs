using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Application.UnitTests.Fakes;

namespace DevAgents.Application.UnitTests.Context;

public class ProjectContextBuilderTests
{
    private static ProjectContextBuilder CreateBuilder(FakeWorkspace workspace, WorkspaceOptions? options = null) =>
        new(workspace, new StaticOptionsMonitor<WorkspaceOptions>(options ?? new WorkspaceOptions()));

    [Test]
    public void Build_WithoutProjectOrFiles_ReturnsTheEmptyContext()
    {
        var context = CreateBuilder(new FakeWorkspace()).Build(null, null, "anything");

        Assert.That(context, Is.SameAs(ProjectContext.None));
        Assert.That(context.Text, Does.EndWith("### RELEVANT PROJECT FILES\n(none)"));
    }

    [Test]
    public void Build_PinnedFiles_AreIncludedFirstEvenOverBudget()
    {
        var builder = CreateBuilder(new FakeWorkspace().With("Program.cs", "var app = 1;"), new WorkspaceOptions { MaxContextChars = 10 });

        var context = builder.Build("Shop", [new InlineFile("notes.txt", new string('x', 500), Pinned: true)], "");

        Assert.That(context.Included, Is.EqualTo(new[] { "notes.txt" }));
        Assert.That(context.TotalFiles, Is.EqualTo(2));
    }

    [Test]
    public void Build_UnpinnedInlineFiles_MustBeSafeSourceFiles()
    {
        InlineFile[] files =
        [
            new("src/Alpha.cs", "class Alpha {}"), new(".env", "KEY=1"), new("notes.txt", "x"),
            new("../Beta.cs", "class Beta {}"), new("cert.pem", "-----", Pinned: true)
        ];

        var context = CreateBuilder(new FakeWorkspace()).Build(null, files, "Alpha");

        Assert.That(context.TotalFiles, Is.EqualTo(2));
        Assert.That(context.Included, Is.EqualTo(new[] { "cert.pem", "src/Alpha.cs" }));
        Assert.That(context.Overview, Does.StartWith("Project: (inline files) (2 indexed files)"));
    }

    [Test]
    public void Build_RanksFilesByRelevanceToTheQuery()
    {
        var workspace = new FakeWorkspace()
            .With("Services/CustomerService.cs", "class CustomerService { }")
            .With("Domain/Order.cs", "class Order { }")
            .With("Services/OrderService.cs", "class OrderService { void Cancel() {} }");

        var context = CreateBuilder(workspace).Build("Shop", null, "Cancel the order in OrderService");

        Assert.That(context.Included, Is.EqualTo(new[] { "Services/OrderService.cs", "Domain/Order.cs" }));
    }

    [Test]
    public void Build_Overview_DescribesProjectsLayersAndFileTree()
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

        Assert.That(context.Overview, Does.StartWith("Project: Shop (2 indexed files)"));
        Assert.That(context.Overview, Does.Contain("- Shop.csproj: net10.0; packages: [MediatR]; references: [Shop.Domain]"));
        Assert.That(context.Overview, Does.Contain("Folder names suggest these layers/patterns: Controllers"));
        Assert.That(context.Overview, Does.EndWith("  Controllers/OrdersController.cs"));
    }

    [Test]
    public void Build_FileLimitReached_Warns()
    {
        var workspace = new FakeWorkspace().With("A.cs", "class A {}").With("B.cs", "class B {}");

        var context = CreateBuilder(workspace, new WorkspaceOptions { MaxFiles = 2 }).Build("Shop", null, "");

        Assert.That(context.Warning, Is.EqualTo("Dosya sayısı sınırına ulaşıldı (2); bazı dosyalar indekslenmedi."));
    }

    [Test]
    public void Build_UnknownProject_IsRejected()
    {
        Assert.Throws<InvalidRequestException>(() => CreateBuilder(new FakeWorkspace()).Build("missing", null, ""));
    }

    [Test]
    public void Tokenize_SplitsIdentifiersAndDropsStopWords()
    {
        Assert.That(QueryTokenizer.Tokenize("Add validation to CreateTodoItemCommand"),
            Is.EqualTo(new[] { "validation", "createtodoitemcommand", "todo", "item", "command" }));
    }
}
