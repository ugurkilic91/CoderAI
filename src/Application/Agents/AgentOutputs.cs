namespace DevAgents.Application.Agents;

/// <summary>Bir çalıştırmada ajanların o ana kadar ürettikleri.</summary>
internal sealed class AgentOutputs
{
    public string Answer { get; set; } = "";

    public string Analysis { get; set; } = "";

    public string Code { get; set; } = "";

    public string Tests { get; set; } = "";

    public string Review { get; set; } = "";

    /// <summary>Son review'un kararı; review çalışmadıysa true.</summary>
    public bool Approved { get; set; } = true;

    /// <summary>Kodlama ajanının review sonrası kaç kez düzeltme yaptığı.</summary>
    public int ReviewRounds { get; set; }

    /// <summary><see cref="Code"/> bir düzeltme turundan mı geliyor.</summary>
    public bool IsCodeRevised => ReviewRounds > 0;
}
