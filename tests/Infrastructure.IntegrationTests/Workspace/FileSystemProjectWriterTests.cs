using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.Workspace;

public class FileSystemProjectWriterTests
{
    private string _root = null!;
    private readonly FileSystemProjectWriter _writer = new(NullLogger<FileSystemProjectWriter>.Instance);

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("devagents-writer-").FullName;
        File.WriteAllText(Path.Combine(_root, "Same.cs"), "class Same {}\n");
        File.WriteAllBytes(Path.Combine(_root, "Bom.cs"), [0xEF, 0xBB, 0xBF, .. "class Bom {}\n"u8.ToArray()]);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private static GeneratedFile Generated(string path, string content) => new(path, content, GeneratedFileKind.Code);

    [Test]
    public async Task ShouldReportCreatedAndModifiedFilesAndSkipUnchangedOnes()
    {
        var changes = await _writer.WriteAsync(_root,
        [
            Generated("src/Features/Hobby.cs", "class Hobby {}"),
            Generated("Bom.cs", "class Bom { int x; }"),
            Generated("Same.cs", "class Same {}")
        ], CancellationToken.None);

        changes.ShouldBe([new FileChange("src/Features/Hobby.cs", FileChangeKind.Created), new FileChange("Bom.cs", FileChangeKind.Modified)]);
        File.ReadAllText(Path.Combine(_root, "src/Features/Hobby.cs")).ShouldBe("class Hobby {}\n");
        // BOM ile kaydedilmiş dosya BOM'unu korur.
        File.ReadAllBytes(Path.Combine(_root, "Bom.cs")).Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [TestCase("../outside.cs")]
    [TestCase(".git/config")]
    [TestCase("src/bin/Debug/x.cs")]
    [TestCase("C:/Windows/x.cs")]
    public async Task ShouldNeverWriteOutsideTheProjectOrIntoToolFolders(string path)
    {
        var changes = await _writer.WriteAsync(_root, [Generated(path, "x")], CancellationToken.None);

        changes.ShouldBeEmpty();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length.ShouldBe(2);
    }
}
