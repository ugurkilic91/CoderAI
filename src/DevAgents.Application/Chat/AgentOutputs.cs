namespace DevAgents.Application.Chat;

/// <summary>What the agents have produced so far in a run.</summary>
internal sealed class AgentOutputs
{
    public string Answer { get; set; } = "";

    public string Analysis { get; set; } = "";

    public string Code { get; set; } = "";

    public string Tests { get; set; } = "";

    public string Review { get; set; } = "";

    /// <summary>Verdict of the last review; true when no review ran.</summary>
    public bool Approved { get; set; } = true;

    /// <summary>How many times the coder revised its work after a review.</summary>
    public int ReviewRounds { get; set; }

    /// <summary>Whether <see cref="Code"/> comes from a revision.</summary>
    public bool IsCodeRevised => ReviewRounds > 0;
}
