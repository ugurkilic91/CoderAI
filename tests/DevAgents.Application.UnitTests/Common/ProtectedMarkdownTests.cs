using DevAgents.Application.Common.Text;

namespace DevAgents.Application.UnitTests.Common;

public class ProtectedMarkdownTests
{
    [Test]
    public void Protect_ReplacesCodeBlocksTogetherWithTheirFileHeaders()
    {
        var markdown = ProtectedMarkdown.Protect("Intro text.\n\n### FILE: src/A.cs\n```csharp\nclass A {}\n```\n\nOutro.");

        Assert.That(markdown.Text, Is.EqualTo("Intro text.\n\n[[CODE_1]]\n\nOutro."));
    }

    [Test]
    public void Restore_PutsCodeBackAndAppendsBlocksTheTranslatorDropped()
    {
        var markdown = ProtectedMarkdown.Protect("One.\n```\nfirst\n```\nTwo.\n```\nsecond\n```");

        var restored = markdown.Restore("Bir. [[CODE_1]] İki.");

        Assert.That(restored, Is.EqualTo("Bir. ```\nfirst\n``` İki.\n\n```\nsecond\n```"));
    }

    [TestCase("```\nonly code\n```", false)]
    [TestCase("Short text.\n\n```\nsome code that is long enough\n```", false)]
    [TestCase("A sentence that is long enough to translate.", true)]
    public void HasProse_NeedsMoreThanTwentyCharactersOutsideCode(string text, bool expected)
    {
        Assert.That(ProtectedMarkdown.Protect(text).HasProse, Is.EqualTo(expected));
    }
}
