using DevAgents.Application.Agents.Parsing;
using DevAgents.Application.Common.Text;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Common;

public class TurkishTextTests
{
    [TestCase("Hobilerimi kaydeden kodu geliştir", true)]
    [TestCase("Sadece testleri yaz", true)]
    [TestCase("record nedir", true)]
    [TestCase("Add an order cancel endpoint", false)]
    [TestCase("Explain how MediatR pipeline behaviours work", false)]
    public void ShouldRecogniseTurkishText(string text, bool expected)
    {
        TurkishText.IsLikelyTurkish(text).ShouldBe(expected);
    }
}

public class MarkdownSectionsTests
{
    private const string Analysis = "### Goal\nStore hobbies.\n\n### Questions for the user\n1. Which fields?\n2. Sort order?\n\n### Edge cases & risks\nNone.";

    [Test]
    public void ShouldReadASectionUntilTheNextHeading()
    {
        AnalysisQuestions.Extract(Analysis).ShouldBe("1. Which fields?\n2. Sort order?");
    }

    [TestCase("### Questions for the user\nNone")]
    [TestCase("### Questions for the user\nNone.")]
    [TestCase("### Goal\nNo questions section at all.")]
    public void ShouldTreatNoneAsNoQuestions(string analysis)
    {
        AnalysisQuestions.Extract(analysis).ShouldBeEmpty();
    }

    [Test]
    public void ShouldRemoveASectionWithItsHeading()
    {
        MarkdownSections.Remove(Analysis, AnalysisQuestions.SectionTitle).ShouldBe("### Goal\nStore hobbies.\n\n### Edge cases & risks\nNone.");
    }
}

public class ModelOutputTests
{
    [Test]
    public void ShouldReplaceLongCodeBlocksAndKeepShortSignatures()
    {
        var longBlock = string.Join("\n", Enumerable.Range(1, 13).Select(line => $"     var x{line} = {line};"));
        var analysis = "### Implementation plan\n1. Entity:\n     ```csharp\n" + longBlock + "\n     ```\n" +
                       "2. Handler signature:\n   ```csharp\n   Task<int> Handle(CreateHobbyCommand request, CancellationToken cancellationToken);\n   ```\n";

        ModelOutput.RemoveLongCodeBlocks(analysis).ShouldBe(
            "### Implementation plan\n1. Entity:\n(code omitted)\n" +
            "2. Handler signature:\n   ```csharp\n   Task<int> Handle(CreateHobbyCommand request, CancellationToken cancellationToken);\n   ```\n");
    }
}
