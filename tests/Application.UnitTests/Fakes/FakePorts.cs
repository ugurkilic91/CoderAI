using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Context;
using DevAgents.Domain.Enums;
using DevAgents.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.UnitTests.Fakes;

/// <summary>Sistem prompt'u olarak prompt'un adını döndürür; testler ajanları böylece ayırt eder.</summary>
internal sealed class FakePromptProvider : IPromptProvider
{
    public string GetPrompt(string name) => name;
}

/// <summary>İstek başka bir model seçmedikçe her ajan "model-&lt;ajan&gt;" ile çalışır.</summary>
internal sealed class FakeModelResolver : IAgentModelResolver
{
    public AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null)
    {
        var name = role.ToString();
        var model = modelOverrides is not null && modelOverrides.TryGetValue(name, out var overridden)
            ? overridden
            : $"model-{name.ToLowerInvariant()}";
        return new AgentModel(name, model, 0.1, 1000, 100);
    }
}

/// <summary>
/// Bellekteki proje klasörleri: "/projects/&lt;ad&gt;". Çözümlenemeyen tek yol "missing"; "Existing" adlı yeni proje zaten var sayılır.
/// <see cref="FakeProjectWriter"/> ile yazılan dosyalar da buradan okunur.
/// </summary>
internal sealed class FakeWorkspace : IWorkspaceFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public FakeWorkspace With(string path, string content)
    {
        Files[path] = content;
        return this;
    }

    public string ResolveProjectPath(string path) =>
        path == "missing" ? throw new ProjectPathException("Proje klasörü bulunamadı: missing")
        : path.StartsWith("/projects/", StringComparison.Ordinal) ? path
        : "/projects/" + path;

    public string ResolveNewProjectDirectory(string? parentPath, string projectName) =>
        projectName == "Existing" ? throw new ProjectPathException("Bu adda bir klasör zaten var: /projects/Existing") : "/projects/" + projectName;

    public IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter) =>
        [.. Files.Where(file => filter.Accepts(file.Key)).Take(filter.MaxFiles).Select(file => ProjectFile.FromContent(file.Key, file.Value))];

    public string? ReadFile(string root, string relativePath) => Files.GetValueOrDefault(relativePath);

    public IReadOnlyList<WorkspaceProject> ListProjects(IEnumerable<string> roots) =>
        [.. roots.Select(root => new WorkspaceProject("App", root + "/App"))];
}

/// <summary>Tek bir "clean-architecture" profili; yeni proje oluşturabilir, test projesi "tests/Unit".</summary>
internal sealed class FakeArchitectureCatalog : IArchitectureCatalog
{
    public static readonly ArchitectureProfile CleanArchitecture = new(
        "clean-architecture", "Clean Architecture", "test profili", "Put entities in src/Domain/Entities.",
        "/templates/clean-architecture", "CleanArchitecture", ["new", "ca-sln", "--output", "{name}"], ["tests/Unit"],
        ["src/Domain/Entities/TodoItem.cs"],
        [new ArchitectureExample("tests/Unit/CleanArchitectureTests.cs", "namespace CleanArchitecture.Tests;")]);

    public IReadOnlyList<ArchitectureProfile> GetAll() => [CleanArchitecture];

    public ArchitectureProfile? Find(string id) => id == CleanArchitecture.Id ? CleanArchitecture : null;
}

/// <summary>Yazılan dosyaları <see cref="FakeWorkspace"/>'e kaydeder ve yazma sırasını tutar.</summary>
internal sealed class FakeProjectWriter(FakeWorkspace workspace) : IProjectWriter
{
    public List<List<string>> Writes { get; } = [];

    public Task<IReadOnlyList<FileChange>> WriteAsync(string projectRoot, IReadOnlyList<GeneratedFile> files, CancellationToken cancellationToken)
    {
        var changes = new List<FileChange>();
        foreach (var file in files)
        {
            if (workspace.Files.TryGetValue(file.Path, out var existing) && existing == file.Content)
                continue;
            changes.Add(new FileChange(file.Path, workspace.Files.ContainsKey(file.Path) ? FileChangeKind.Modified : FileChangeKind.Created));
            workspace.Files[file.Path] = file.Content;
        }
        Writes.Add([.. files.Select(file => file.Path)]);
        return Task.FromResult<IReadOnlyList<FileChange>>(changes);
    }
}

/// <summary>Anlık görüntüden sonra yazılan dosyaları değişiklik olarak bildirir.</summary>
internal sealed class FakeChangeTracker(FakeWorkspace workspace) : IChangeTracker
{
    public const string Snapshot = "0123456789abcdef0123456789abcdef01234567";

    private HashSet<string> _before = [];

    public int Snapshots { get; private set; }

    public Task<string> SnapshotAsync(string projectRoot, string message, CancellationToken cancellationToken)
    {
        Snapshots++;
        _before = [.. workspace.Files.Keys];
        return Task.FromResult(Snapshot);
    }

    public Task<IReadOnlyList<FileChange>> GetChangesAsync(string projectRoot, string snapshot, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FileChange>>(
            [.. workspace.Files.Keys.Where(path => !_before.Contains(path)).Order().Select(path => new FileChange(path, FileChangeKind.Created))]);

    public Task<IReadOnlyList<FileChange>> RevertAsync(string projectRoot, string snapshot, CancellationToken cancellationToken) =>
        GetChangesAsync(projectRoot, snapshot, cancellationToken);
}

/// <summary>İçinde "SYNTAX_ERROR" geçen .cs dosyaları için bir sözdizimi hatası bildirir.</summary>
internal sealed class FakeSyntaxChecker : ICodeSyntaxChecker
{
    public IReadOnlyList<CompilerDiagnostic> Check(IEnumerable<GeneratedFile> files) =>
        [.. files.Where(file => file.Content.Contains("SYNTAX_ERROR", StringComparison.Ordinal))
            .Select(file => new CompilerDiagnostic(file.Path, 1, 1, "CS1002", "; expected"))];
}

/// <summary>Sırayla önceden yazılmış derleme ve test sonuçlarını döndürür (son sonuç tekrar eder).</summary>
internal sealed class FakeBuildSandbox : IBuildSandbox
{
    private readonly Queue<BuildResult> _builds = new();
    private readonly Queue<TestRunResult> _tests = new();

    public List<string> CreatedProjects { get; } = [];

    public int Builds { get; private set; }

    public static BuildResult Success { get; } = new(true, [], "Build succeeded.", 1);

    public static TestRunResult Passed(int count) => new(count, count, 0, 0, [], "Passed!", 1);

    public FakeBuildSandbox Build(params BuildResult[] results)
    {
        foreach (var result in results)
            _builds.Enqueue(result);
        return this;
    }

    public FakeBuildSandbox Test(params TestRunResult[] results)
    {
        foreach (var result in results)
            _tests.Enqueue(result);
        return this;
    }

    public Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SandboxStatus("Docker", true, "test"));

    public Task<string> CreateProjectAsync(ArchitectureProfile architecture, string parentDirectory, string projectName,
        Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        CreatedProjects.Add(projectName);
        return Task.FromResult($"{parentDirectory}/{projectName}");
    }

    public Task<BuildResult> BuildAsync(string projectRoot, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        Builds++;
        return Task.FromResult(Next(_builds, Success));
    }

    public Task<TestRunResult> RunTestsAsync(string projectRoot, IReadOnlyList<string> testProjects, Func<string, Task>? onOutput, CancellationToken cancellationToken) =>
        Task.FromResult(Next(_tests, Passed(1)));

    private static T Next<T>(Queue<T> queue, T fallback) =>
        queue.Count switch { 0 => fallback, 1 => queue.Peek(), _ => queue.Dequeue() };
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
