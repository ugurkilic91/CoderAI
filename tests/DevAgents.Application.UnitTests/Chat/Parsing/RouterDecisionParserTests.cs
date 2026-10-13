using DevAgents.Application.Chat.Parsing;
using DevAgents.Domain.Agents;

namespace DevAgents.Application.UnitTests.Chat.Parsing;

public class RouterDecisionParserTests
{
    [Test]
    public void Parse_JsonWrappedInProseAndFences_ReturnsThePlan()
    {
        var plan = RouterDecisionParser.Parse("Sure!\n```json\n{\"intent\":\"bug_fix\",\"code\":true,\"test\":true,\"reason\":\"Hata.\"}\n```");

        Assert.That(plan, Is.EqualTo(new AgentPlan("bug_fix", false, false, true, true, false, NeedsContext: true, "Hata.")));
    }

    [Test]
    public void Parse_ExplicitlyNoContextAndNoIntent_IsKept()
    {
        var plan = RouterDecisionParser.Parse("""{"answer":true,"needsContext":false}""");

        Assert.That(plan!.NeedsContext, Is.False);
        Assert.That(plan.Intent, Is.EqualTo("custom"));
    }

    [Test]
    public void Parse_AppliesThePlanningRules()
    {
        var plan = RouterDecisionParser.Parse("""{"intent":"question","answer":true,"code":true}""");

        Assert.That(plan!.Answer, Is.False);
        Assert.That(plan.Code, Is.True);
    }

    [Test]
    public void Parse_OnlyJsonTrueEnablesAnAgent()
    {
        var plan = RouterDecisionParser.Parse("""{"code":"yes","test":1,"review":true}""");

        Assert.That(plan!.Code, Is.False);
        Assert.That(plan.Test, Is.False);
        Assert.That(plan.Review, Is.True);
    }

    [TestCase("no json here")]
    [TestCase("{\"intent\": \"feature\", \"code\": tru")]
    [TestCase("{ not json }")]
    public void Parse_UnreadableOutput_ReturnsNull(string output)
    {
        Assert.That(RouterDecisionParser.Parse(output), Is.Null);
    }
}
