namespace DevAgents.Domain.ValueObjects;

/// <summary>
/// Bir istek için hangi ajanların çalışacağı. Router ajanı karar verir ya da kullanıcı zorlar.
/// İstemcilere olduğu gibi JSON olarak gönderilir; bu yüzden yeni public özellik eklemeyin, metot kullanın.
/// </summary>
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
    /// <summary>Tüm kod ajanları: analiz → kod → test → review, proje bağlamıyla.</summary>
    public static AgentPlan Full(string reason) =>
        new("feature", Answer: false, Analyze: true, Code: true, Test: true, Review: true, NeedsContext: true, reason);

    /// <summary>Kullanıcının elle seçtiği aşamalar; proje bağlamı her zaman verilir.</summary>
    public static AgentPlan Manual(bool answer, bool analyze, bool code, bool test, bool review, string reason) =>
        new("manual", answer, analyze, code, test, review, NeedsContext: true, reason);

    /// <summary>
    /// Router'ın önerdiği plana planlama kurallarını uygular:
    /// hiçbir ajanın çalışmadığı bir plan olmaz, düz cevap da kod ajanlarıyla birlikte çalışmaz.
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

    // Özellik değil metot: plan JSON'a olduğu gibi yazılıyor.
    public bool RunsAnyAgent() => Answer || Analyze || Code || Test || Review;
}
