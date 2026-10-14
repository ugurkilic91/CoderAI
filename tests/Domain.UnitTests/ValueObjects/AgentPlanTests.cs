using DevAgents.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Domain.UnitTests.ValueObjects;

public class AgentPlanTests
{
    [Test]
    public void ShouldRunTheAnswerAgentWhenThePlanRunsNothing()
    {
        var plan = new AgentPlan("chat", false, false, false, false, false, NeedsContext: false, "");

        plan.Normalize().Answer.ShouldBeTrue();
    }

    [Test]
    public void ShouldDropTheAnswerWhenCodeAgentsRun()
    {
        var plan = new AgentPlan("question", Answer: true, Analyze: false, Code: true, Test: false, Review: true, NeedsContext: true, "");

        plan.Normalize().ShouldBe(plan with { Answer = false });
    }

    [Test]
    public void ShouldKeepAnAnswerTogetherWithAnalysis()
    {
        var plan = new AgentPlan("question", Answer: true, Analyze: true, Code: false, Test: false, Review: false, NeedsContext: true, "");

        plan.Normalize().ShouldBe(plan);
    }

    [Test]
    public void FullPlanShouldRunEveryCodeAgentWithContext()
    {
        AgentPlan.Full("why").ShouldBe(new AgentPlan("feature", false, true, true, true, true, NeedsContext: true, "why"));
    }

    [Test]
    public void ManualPlanShouldKeepTheChosenStagesAsTheyAre()
    {
        var plan = AgentPlan.Manual(answer: false, analyze: false, code: false, test: false, review: false, "none");

        plan.RunsAnyAgent().ShouldBeFalse();
        plan.NeedsContext.ShouldBeTrue();
    }
}
