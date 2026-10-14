using System.Diagnostics;
using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.SourceControl;
using DevAgents.Infrastructure.Storage;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.SourceControl;

/// <summary>Gerçek git ile: anlık görüntü, değişiklik listesi ve geri alma; projenin kendi git deposuna dokunulmaz.</summary>
public class GitChangeTrackerTests
{
    private string _project = null!;
    private string _data = null!;
    private GitChangeTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _project = Directory.CreateTempSubdirectory("devagents-project-").FullName;
        _data = Directory.CreateTempSubdirectory("devagents-data-").FullName;
        _tracker = new GitChangeTracker(new StaticOptionsMonitor<StorageOptions>(new StorageOptions { DataDirectory = _data }));
        Write("src/Existing.cs", "class Existing {}");
        Write("src/ToDelete.cs", "class ToDelete {}");
        Write("bin/Debug/app.dll", "binary");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_project, recursive: true);
        Directory.Delete(_data, recursive: true);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_project, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(_project, path));

    [Test]
    public async Task ShouldListWhatChangedSinceTheSnapshotAndRevertIt()
    {
        var snapshot = await _tracker.SnapshotAsync(_project, "before", CancellationToken.None);
        Write("src/Existing.cs", "class Existing { int x; }");
        Write("src/Features/Hobby.cs", "class Hobby {}");
        File.Delete(Path.Combine(_project, "src/ToDelete.cs"));
        Write("bin/Debug/app.dll", "rebuilt");

        var changes = await _tracker.GetChangesAsync(_project, snapshot, CancellationToken.None);

        snapshot.Length.ShouldBe(40);
        // Derleme çıktıları (bin/) izlenmez.
        changes.ShouldBe(
        [
            new FileChange("src/Existing.cs", FileChangeKind.Modified),
            new FileChange("src/Features/Hobby.cs", FileChangeKind.Created),
            new FileChange("src/ToDelete.cs", FileChangeKind.Deleted)
        ], ignoreOrder: true);

        var reverted = await _tracker.RevertAsync(_project, snapshot, CancellationToken.None);

        reverted.Count.ShouldBe(3);
        Read("src/Existing.cs").ShouldBe("class Existing {}");
        Read("src/ToDelete.cs").ShouldBe("class ToDelete {}");
        File.Exists(Path.Combine(_project, "src/Features/Hobby.cs")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_project, "src/Features")).ShouldBeFalse();
        (await _tracker.GetChangesAsync(_project, snapshot, CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task ShouldNotTouchTheProjectsOwnGitRepository()
    {
        Git("init", "--quiet");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--allow-empty", "--quiet", "-m", "user commit");
        var userHead = Git("rev-parse", "HEAD");

        var snapshot = await _tracker.SnapshotAsync(_project, "before", CancellationToken.None);
        Write("src/New.cs", "class New {}");
        await _tracker.GetChangesAsync(_project, snapshot, CancellationToken.None);

        Git("rev-parse", "HEAD").ShouldBe(userHead);
        Git("status", "--porcelain").ShouldContain("?? src/");
    }

    private string Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = _project, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
