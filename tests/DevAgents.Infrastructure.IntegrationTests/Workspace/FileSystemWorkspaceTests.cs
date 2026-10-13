using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Infrastructure.Workspace;

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
    public void ResolveProjectPath_FolderName_IsResolvedUnderTheFirstRoot()
    {
        Assert.That(CreateWorkspace(_root).ResolveProjectPath("Shop"), Is.EqualTo(Path.Combine(_root, "Shop")));
    }

    [Test]
    public void ResolveProjectPath_QuotedAbsolutePathInsideARoot_IsAccepted()
    {
        var billing = Path.Combine(_root, "Billing");

        Assert.That(CreateWorkspace(_root).ResolveProjectPath($" \"{billing}\" "), Is.EqualTo(billing));
    }

    [Test]
    public void ResolveProjectPath_PathOutsideTheRoots_IsRejected()
    {
        var workspace = CreateWorkspace(Path.Combine(_root, "Shop"));

        var exception = Assert.Throws<InvalidRequestException>(() => workspace.ResolveProjectPath(Path.Combine(_root, "Billing")));

        Assert.That(exception!.Message, Does.StartWith("Yol izin verilen kök dizinlerin dışında"));
    }

    [Test]
    public void ResolveProjectPath_SiblingFolderWithTheSamePrefix_IsRejected()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ShopBackup"));
        var workspace = CreateWorkspace(Path.Combine(_root, "Shop"));

        Assert.Throws<InvalidRequestException>(() => workspace.ResolveProjectPath(Path.Combine(_root, "ShopBackup")));
    }

    [Test]
    public void ResolveProjectPath_EscapingTheRootWithDotDot_IsRejected()
    {
        Assert.Throws<InvalidRequestException>(() => CreateWorkspace(Path.Combine(_root, "Shop")).ResolveProjectPath("../Billing"));
    }

    [Test]
    public void ResolveProjectPath_MissingFolder_IsRejected()
    {
        var exception = Assert.Throws<InvalidRequestException>(() => CreateWorkspace(_root).ResolveProjectPath("Nope"));

        Assert.That(exception!.Message, Is.EqualTo($"Proje klasörü bulunamadı: {Path.Combine(_root, "Nope")}"));
    }

    [Test]
    public void ResolveProjectPath_WithoutRoots_AnyExistingFolderIsAllowed()
    {
        var shop = Path.Combine(_root, "Shop");

        Assert.That(CreateWorkspace().ResolveProjectPath(shop), Is.EqualTo(shop));
    }

    [Test]
    public void EnumerateFiles_SkipsToolFoldersSecretsAndEmptyOrLargeFiles()
    {
        var files = CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter());

        Assert.That(files.Select(file => file.RelativePath), Is.EquivalentTo(new[] { "Shop.csproj", "Program.cs", "Services/OrderService.cs" }));
        Assert.That(files.Single(file => file.RelativePath == "Program.cs").ReadContent(), Is.EqualTo("var app = 1;"));
    }

    [Test]
    public void EnumerateFiles_StopsAtTheFileLimit()
    {
        Assert.That(CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter(maxFiles: 2)), Has.Count.EqualTo(2));
    }

    [Test]
    public void EnumerateFiles_FileDeletedAfterIndexing_ReadsAsEmpty()
    {
        var program = CreateWorkspace().EnumerateFiles(Path.Combine(_root, "Shop"), Filter()).Single(file => file.RelativePath == "Program.cs");
        File.Delete(Path.Combine(_root, "Shop", "Program.cs"));

        Assert.That(program.ReadContent(), Is.Empty);
    }

    [Test]
    public void ListProjects_ReturnsTheSubfoldersOfExistingRootsByName()
    {
        var projects = CreateWorkspace().ListProjects([_root, Path.Combine(_root, "missing")]);

        Assert.That(projects.Select(project => project.Name), Is.EqualTo(new[] { "Billing", "Shop" }));
        Assert.That(projects[1].Path, Is.EqualTo(Path.Combine(_root, "Shop")));
    }
}
