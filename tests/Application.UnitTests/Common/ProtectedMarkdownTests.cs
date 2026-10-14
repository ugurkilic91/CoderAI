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

    [Test]
    public void ShouldProtectIndentedCodeBlocksInsideLists()
    {
        var markdown = "1. Entity:\n   ```csharp\n   public class KKEGOR { }\n   ```\n2. Done.";

        var protectedMarkdown = ProtectedMarkdown.Protect(markdown);

        protectedMarkdown.Text.ShouldBe("1. Entity:\n[[CODE_1]]\n2. Done.");
        protectedMarkdown.Restore("1. Varlık:\n[[CODE_1]]\n2. Bitti.")
            .ShouldBe("1. Varlık:\n   ```csharp\n   public class KKEGOR { }\n   ```\n2. Bitti.");
    }

    [Test]
    public void ShouldDropPlaceholdersTheTranslatorInvented()
    {
        ProtectedMarkdown.Protect("- No DI registration needed.").Restore("- DI kaydı gerekmez.\n[[CODE_1]] [[CODE_2]]")
            .ShouldBe("- DI kaydı gerekmez.");
    }
}
