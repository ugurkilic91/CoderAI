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
    /// <summary>İlk anlık görüntünün kimliği (sonrakiler sırayla numaralanır).</summary>
    public const string Snapshot = "0123456789abcdef0123456789abcdef01234567";

    private readonly List<Dictionary<string, string>> _snapshots = [];

    public int Snapshots => _snapshots.Count;

    public Task<string> SnapshotAsync(string projectRoot, string message, CancellationToken cancellationToken)
    {
        _snapshots.Add(new Dictionary<string, string>(workspace.Files, StringComparer.Ordinal));
        return Task.FromResult(_snapshots.Count == 1 ? Snapshot : (_snapshots.Count - 1).ToString("x40"));
    }

    public Task<IReadOnlyList<FileChange>> GetChangesAsync(string projectRoot, string snapshot, CancellationToken cancellationToken) =>
        Task.FromResult(Changes(Find(snapshot)));

    /// <summary>Çalışma alanını anlık görüntüdeki hâline döndürür.</summary>
    public Task<IReadOnlyList<FileChange>> RevertAsync(string projectRoot, string snapshot, CancellationToken cancellationToken)
    {
        var before = Find(snapshot);
        var changes = Changes(before);
        foreach (var change in changes)
        {
            if (before.TryGetValue(change.Path, out var content))
                workspace.Files[change.Path] = content;
            else
                workspace.Files.Remove(change.Path);
        }
        return Task.FromResult(changes);
    }

    private Dictionary<string, string> Find(string snapshot) =>
        _snapshots[snapshot == Snapshot ? 0 : Convert.ToInt32(snapshot, 16)];

    private IReadOnlyList<FileChange> Changes(Dictionary<string, string> before) =>
    [
        .. workspace.Files.Keys.Union(before.Keys).Order(StringComparer.Ordinal)
            .Select(path => (path, now: workspace.Files.GetValueOrDefault(path), then: before.GetValueOrDefault(path)))
            .Where(file => file.now != file.then)
            .Select(file => new FileChange(file.path,
                file.then is null ? FileChangeKind.Created : file.now is null ? FileChangeKind.Deleted : FileChangeKind.Modified))
    ];
}

/// <summary>İçinde "SYNTAX_ERROR" geçen .cs dosyaları için bir sözdizimi hatası bildirir.</summary>
internal sealed class FakeSyntaxChecker : ICodeSyntaxChecker
{
    public IReadOnlyList<CompilerDiagnostic> Check(IEnumerable<GeneratedFile> files) =>
        [.. files.Where(file => file.Content.Contains("SYNTAX_ERROR", StringComparison.Ordinal))
            .Select(file => new CompilerDiagnostic(file.Path, 1, 1, "CS1002", "; expected"))];
}

/// <summary>
/// Tanımlanan dosyalarda CS0246 hatası varsa başına "using &lt;namespace&gt;;" ekler (dosyada yoksa).
/// Varsayılan olarak hiçbir şeyi düzeltmez; böylece diğer testlerde hatalar düzeltme ajanına gider.
/// </summary>
internal sealed class FakeUsingFixer(FakeWorkspace workspace) : IUsingDirectiveFixer
{
    private readonly Dictionary<string, string> _namespaces = new(StringComparer.Ordinal);

    public int Calls { get; private set; }

    public FakeUsingFixer Resolve(string path, string ns)
    {
        _namespaces[path] = ns;
        return this;
    }

    public Task<IReadOnlyList<UsingFix>> FixAsync(string projectRoot, IReadOnlyList<CompilerDiagnostic> errors, CancellationToken cancellationToken)
    {
        Calls++;
        var fixes = new List<UsingFix>();
        foreach (var path in errors.Where(error => error.Code == "CS0246").Select(error => error.File).Distinct())
        {
            if (_namespaces.TryGetValue(path, out var ns) && workspace.ReadFile(projectRoot, path) is { } content
                && !content.Contains($"using {ns};", StringComparison.Ordinal))
                fixes.Add(new UsingFix(path, $"using {ns};\n{content}", [ns]));
        }
        return Task.FromResult<IReadOnlyList<UsingFix>>(fixes);
    }
}

/// <summary>Satır tabanlı basit geri ekleme: üyenin eski hâldeki satırını son kapanış parantezinin önüne koyar.</summary>
internal sealed class FakeMemberRestorer : ICodeMemberRestorer
{
    public MemberRestore? Restore(string original, string current, IReadOnlyCollection<MissingMember> missing)
    {
        var restored = new List<string>();
        foreach (var member in missing)
        {
            var line = original.Split('\n').FirstOrDefault(text => text.Contains(member.MemberName, StringComparison.Ordinal));
            if (line is null || !current.Contains(member.TypeName, StringComparison.Ordinal)
                || current.Contains(member.MemberName, StringComparison.Ordinal))
                continue;
            current = current.Insert(current.LastIndexOf('}'), line + "\n");
            restored.Add($"{member.TypeName}.{member.MemberName}");
        }
        return restored.Count == 0 ? null : new MemberRestore(current, restored);
    }
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
