namespace DevAgents.Domain.Enums;

/// <summary>
/// Akışta görev alan ajanlar. Adları aynı zamanda yapılandırmadaki model ayarlarının
/// (<c>Ollama:Agents:&lt;ad&gt;</c>) ve istek bazında model seçiminin anahtarıdır.
/// </summary>
public enum AgentRole
{
    /// <summary>İsteği okuyup hangi ajanların çalışacağına karar verir.</summary>
    Router,

    /// <summary>Türkçe isteği İngilizceye çevirir (modeller İngilizcede daha iyi kod üretiyor).</summary>
    Translator,

    /// <summary>Mimariyi inceleyip plan çıkarır; düz sorulara da bu ajan cevap verir.</summary>
    Analyzer,

    /// <summary>Kodu yazar ve review sonrası düzeltir.</summary>
    Coder,

    /// <summary>Birim testlerini yazar.</summary>
    Tester,

    /// <summary>Kodu ve testleri inceler, onay ya da değişiklik ister.</summary>
    Reviewer,

    /// <summary>İngilizce raporu Türkçeye çevirir.</summary>
    BackTranslator
}

public static class AgentRoleExtensions
{
    /// <summary>
    /// Ajanın kendi model ayarı yoksa hangi ajanın ayarını kullanacağı.
    /// Router ve geri çeviri, çevirmen gibi hafif dil işleri olduğu için çevirmenin ayarını kullanır.
    /// </summary>
    public static AgentRole? SettingsFallback(this AgentRole role) =>
        role is AgentRole.Router or AgentRole.BackTranslator ? AgentRole.Translator : null;
}
