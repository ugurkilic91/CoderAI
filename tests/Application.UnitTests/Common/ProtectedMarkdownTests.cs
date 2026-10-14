using DevAgents.Application.Common.Text;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Common;

public class ProtectedMarkdownTests
{
    [Test]
    public void ShouldReplaceCodeBlocksTogetherWithTheirFileHeaders()
    {
        var markdown = ProtectedMarkdown.Protect("Intro text.\n\n### FILE: src/A.cs\n```csharp\nclass A {}\n```\n\nOutro.");

        markdown.Text.ShouldBe("Intro text.\n\n[[CODE_1]]\n\nOutro.");
    }

    [Test]
    public void ShouldPutCodeBackAndAppendBlocksTheTranslatorDropped()
    {
        var markdown = ProtectedMarkdown.Protect("One.\n```\nfirst\n```\nTwo.\n```\nsecond\n```");

        markdown.Restore("Bir. [[CODE_1]] İki.").ShouldBe("Bir. ```\nfirst\n``` İki.\n\n```\nsecond\n```");
    }

    [TestCase("```\nonly code\n```", false)]
    [TestCase("Short text.\n\n```\nsome code that is long enough\n```", false)]
    [TestCase("A sentence that is long enough to translate.", true)]
    public void ShouldNeedMoreThanTwentyCharactersOutsideCodeToTranslate(string text, bool expected)
    {
        ProtectedMarkdown.Protect(text).HasProse.ShouldBe(expected);
    }
}
