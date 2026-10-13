using DevAgents.Application.Chat.Parsing;
using DevAgents.Domain.Files;

namespace DevAgents.Application.UnitTests.Chat.Parsing;

public class GeneratedFileParserTests
{
    [Test]
    public void Parse_ReadsFileBlocksAndSkipsUnsafePaths()
    {
        const string markdown =
            "### FILE: src/A.cs\n```csharp\nclass A {}\n```\n\n" +
            "### FILE: ../evil.cs\n```csharp\nbad\n```\n\n" +
            "### FILE: `./src/B.cs`\r\n```csharp\r\nclass B {}\r\n```\n\n" +
            "### NOTES\n- none";

        var files = GeneratedFileParser.Parse(markdown, GeneratedFileKind.Code);

        Assert.That(files, Is.EqualTo(new[]
        {
            new GeneratedFile("src/A.cs", "class A {}", GeneratedFileKind.Code),
            new GeneratedFile("src/B.cs", "class B {}", GeneratedFileKind.Code)
        }));
    }

    [Test]
    public void Parse_CodeBlockWithoutFileHeader_IsNotAFile()
    {
        Assert.That(GeneratedFileParser.Parse("Example:\n```csharp\nclass A {}\n```", GeneratedFileKind.Test), Is.Empty);
    }
}
