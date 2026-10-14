using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Infrastructure.Workspace;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.Workspace;

public class FileSystemWorkspaceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("devagents-tests-").FullName;
        Write("Shop/Shop.csproj", "<Project />");
        Write("Shop/Program.cs", "var app = 1;");
        Write("Shop/Services/OrderService.cs", "class OrderService {}");
        Write("Shop/bin/Debug/Generated.cs", "class Generated {}");
        Write("Shop/node_modules/lib/index.js", "module.exports = {};");
        Write("Shop/.env", "KEY=1");
        Write("Shop/appsettings.Production.json", "{}");
        Write("Shop/notes.txt", "notes");
        Write("Shop/Empty.cs", "");
        Write("Shop/Big.cs", new string('x', 300));
        Write("Billing/README.md", "# Billing");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static FileSystemWorkspace CreateWorkspace(params string[] allowedRoots) =>
        new(new StaticOptionsMonitor<WorkspaceOptions>(new WorkspaceOptions { AllowedRoots = allowedRoots }));

    private static ProjectFileFilter Filter(int maxFiles = 5000) =>
        ProjectFileFilter.FromOptions(new WorkspaceOptions { MaxFiles = maxFiles, MaxFileBytes = 200 });

    [Test]
    public void ShouldResolveAFolderNameUnderTheFirstRoot()
    {
        CreateWorkspace(_root).ResolveProjectPath("Shop").ShouldBe(Path.Combine(_root, "Shop"));
    }

    [Test]
    public void ShouldAcceptAQuotedAbsolutePathInsideARoot()
    {
        var billing = Path.Combine(_root, "Billing");

        CreateWorkspace(_root).ResolveProjectPath($" \"{billing}\" ").ShouldBe(billing);
    }

    [Test]
    public void ShouldRejectAPathOutsideTheRoots()
    {
        var workspace = CreateWorkspace(Path.Combine(_root, "Shop"));

        Should.Throw<ProjectPathException>(() => workspace.ResolveProjectPath(Path.Combine(_root, "Billing")))
            .Message.ShouldStartWith("Yol izin verilen kök dizinlerin dışında");
    }

    [Test]
    public void ShouldRejectASiblingFolderWithTheSamePrefix()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ShopBackup"));
        var workspace = CreateWorkspace(Path.Combine(_root, "Shop"));

        Should.Throw<ProjectPathException>(() => workspace.ResolveProjectPath(Path.Combine(_root, "ShopBackup")));
    }

    [Test]
    public void ShouldRejectEscapingTheRootWithDotDot()
    {
        Should.Throw<ProjectPathException>(() => CreateWorkspace(Path.Combine(_root, "Shop")).ResolveProjectPath("../Billing"));
    }

    [Test]
    public void ShouldRejectAMissingFolder()
    {
        Should.Throw<ProjectPathException>(() => CreateWorkspace(_root).ResolveProjectPath("Nope"))
            .Message.ShouldBe($"Proje klasörü bulunamadı: {Path.Combine(_root, "Nope")}");
    }

    [Test]
    public void ShouldAllowAnyExistingFolderWithoutRoots()
    {
        var shop = Path.Combine(_root, "Shop");

        CreateWorkspace().ResolveProjectPath(shop).ShouldBe(shop);
    }

    [Test]
    public void ShouldSkipToolFoldersSecretsAndEmptyOrLargeFiles()
    {
        var files = CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter());

        files.Select(file => file.RelativePath).ShouldBe(["Shop.csproj", "Program.cs", "Services/OrderService.cs"], ignoreOrder: true);
        files.Single(file => file.RelativePath == "Program.cs").ReadContent().ShouldBe("var app = 1;");
    }

    [Test]
    public void ShouldStopAtTheFileLimit()
    {
        CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter(maxFiles: 2)).Count.ShouldBe(2);
    }

    [Test]
    public void ShouldReadAFileDeletedAfterIndexingAsEmpty()
    {
        var program = CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter()).Single(file => file.RelativePath == "Program.cs");
        File.Delete(Path.Combine(_root, "Shop", "Program.cs"));

        program.ReadContent().ShouldBeEmpty();
    }

    [Test]
    public void ShouldListTheSubfoldersOfExistingRootsByName()
    {
        var projects = CreateWorkspace().ListProjects([_root, Path.Combine(_root, "missing")]);

        projects.Select(project => project.Name).ShouldBe(["Billing", "Shop"]);
        projects[1].Path.ShouldBe(Path.Combine(_root, "Shop"));
    }
}
