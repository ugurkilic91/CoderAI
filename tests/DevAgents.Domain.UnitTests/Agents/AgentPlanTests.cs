using DevAgents.Domain.Agents;

namespace DevAgents.Domain.UnitTests.Agents;

public class AgentPlanTests
{
    [Test]
    public void Normalize_PlanWithoutAgents_RunsTheAnswerAgent()
    {
        var plan = new AgentPlan("chat", false, false, false, false, false, NeedsContext: false, "");

        Assert.That(plan.Normalize().Answer, Is.True);
    }

    [Test]
    public void Normalize_AnswerCombinedWithCodeAgents_DropsTheAnswer()
    {
        var plan = new AgentPlan("question", Answer: true, Analyze: false, Code: true, Test: false, Review: true, NeedsContext: true, "");

        var normalized = plan.Normalize();

        Assert.That(normalized, Is.EqualTo(plan with { Answer = false }));
    }

    [Test]
    public void Normalize_AnswerWithAnalysis_KeepsBoth()
    {
        var plan = new AgentPlan("question", Answer: true, Analyze: true, Code: false, Test: false, Review: false, NeedsContext: true, "");

        Assert.That(plan.Normalize(), Is.EqualTo(plan));
    }

    [Test]
    public void Full_RunsEveryCodeAgentWithContext()
    {
        var plan = AgentPlan.Full("why");

        Assert.That(plan, Is.EqualTo(new AgentPlan("feature", false, true, true, true, true, NeedsContext: true, "why")));
    }

    [Test]
    public void Manual_KeepsTheChosenStagesAsTheyAre()
    {
        var plan = AgentPlan.Manual(answer: false, analyze: false, code: false, test: false, review: false, "none");

        Assert.That(plan.RunsAnyAgent(), Is.False);
        Assert.That(plan.NeedsContext, Is.True);
    }
}
