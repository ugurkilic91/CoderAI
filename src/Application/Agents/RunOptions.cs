namespace DevAgents.Application.Agents;

/// <summary>Yapılandırmadaki akış davranışını bu istek için değiştiren ayarlar.</summary>
/// <param name="ModelOverrides">Ajan adına göre model, örn. <c>{ "Coder": "qwen2.5-coder:14b" }</c>.</param>
/// <param name="Mode"><see cref="RunModes"/> değerlerinden biri; <paramref name="Stages"/> verilmişse dikkate alınmaz.</param>
/// <param name="Stages">Yalnızca bu aşamalar çalışır: answer, analyze, code, test, review.</param>
/// <param name="AskQuestions">
/// true: analiz ajanı isteği uygulamak için kritik bir bilgi eksik görürse kod yazılmadan önce kullanıcıya soru sorulur
/// (soru-cevap akışı). Varsayılan false: ajan makul varsayımlarla devam eder.
/// </param>
public sealed record RunOptions(
    int? MaxReviewIterations = null,
    IReadOnlyDictionary<string, string>? ModelOverrides = null,
    bool? TranslateOutput = null,
    string? Mode = null,
    IReadOnlyList<string>? Stages = null,
    bool? AskQuestions = null);

public static class RunModes
{
    /// <summary>Hangi ajanların çalışacağına Router karar verir (varsayılan).</summary>
    public const string Auto = "auto";

    /// <summary>Tüm kod ajanları çalışır: analiz → kod → test → review.</summary>
    public const string Full = "full";
}

/// <summary>Önceki bir sohbet turu; "şimdi doğrulama da ekle" gibi devam istekleri bağlamı kaybetmesin diye.</summary>
/// <param name="Questions">O turda ajanın kullanıcıya sorduğu sorular (varsa); kullanıcının cevabı bu soruların cevabıdır.</param>
public sealed record HistoryItem(string? Request, string? Code, string? Questions = null);
