namespace DevAgents.Domain.Agents;

/// <summary>Which agents run for a request (decided by the Router agent, or forced by the user).</summary>
public sealed record AgentPlan(
    string Intent,
    bool Answer,
    bool Analyze,
    bool Code,
    bool Test,
    bool Review,
    bool NeedsContext,
    string Reason)
{
    /// <summary>Every code agent: analysis → code → test → review, with project context.</summary>
    public static AgentPlan Full(string reason) =>
        new("feature", Answer: false, Analyze: true, Code: true, Test: true, Review: true, NeedsContext: true, reason);

    /// <summary>The stages the user picked; always with project context.</summary>
    public static AgentPlan Manual(bool answer, bool analyze, bool code, bool test, bool review, string reason) =>
        new("manual", answer, analyze, code, test, review, NeedsContext: true, reason);

    /// <summary>
    /// Applies the planning rules to a plan proposed by the router: a plan never runs nothing,
    /// and a plain answer is never combined with the code agents.
    /// </summary>
    public AgentPlan Normalize()
    {
        var plan = this;
        if (!plan.RunsAnyAgent())
            plan = plan with { Answer = true };
        if (plan.Answer && (plan.Code || plan.Test || plan.Review))
            plan = plan with { Answer = false };
        return plan;
    }

    // A method rather than a property: plans are serialized to clients as they are.
    public bool RunsAnyAgent() => Answer || Analyze || Code || Test || Review;
}
