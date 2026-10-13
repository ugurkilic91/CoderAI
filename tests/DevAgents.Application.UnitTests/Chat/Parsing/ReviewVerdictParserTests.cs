using DevAgents.Application.Chat.Parsing;

namespace DevAgents.Application.UnitTests.Chat.Parsing;

public class ReviewVerdictParserTests
{
    [TestCase("VERDICT: APPROVED\n### Issues\nNone", true)]
    [TestCase("verdict:approved", true)]
    [TestCase("VERDICT: NEEDS_CHANGES\n### Issues\n[MAJOR] ...", false)]
    [TestCase("VERDICT: needs changes", false)]
    [TestCase("VERDICT: APPROVED, but VERDICT: NEEDS_CHANGES after all", false)]
    [TestCase("Looks good to me.", false)]
    public void IsApproved_ReadsTheVerdict(string review, bool expected)
    {
        Assert.That(ReviewVerdictParser.IsApproved(review), Is.EqualTo(expected));
    }

    [Test]
    public void IsApproved_VerdictAfterTheFirst400Characters_DoesNotCount()
    {
        Assert.That(ReviewVerdictParser.IsApproved(new string('x', 400) + "VERDICT: APPROVED"), Is.False);
    }
}
