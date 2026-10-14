using DevAgents.Application.Context;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Agents;

/// <summary>Bir akış çalıştırmasının sonucu ("final" olayının verisi).</summary>
/// <param name="EnglishRequest">Ajanların gördüğü, İngilizceye çevrilmiş istek.</param>
/// <param name="Approved">Son review'un kararı; review çalışmadıysa true.</param>
/// <param name="FinalMarkdown">Kullanıcıya gösterilen rapor (çeviri kapalı değilse Türkçe).</param>
/// <param name="Files">
/// Kodlama ve test ajanlarının ürettiği dosyalar. Kod projeye uygulandıysa, düzeltmelerden sonraki son hâlleridir.
/// </param>
/// <param name="Models">Hangi ajanın hangi modelle çalıştığı.</param>
/// <param name="Seconds">Toplam süre.</param>
/// <param name="Questions">Analiz ajanının kod yazmadan önce sorduğu sorular; boşsa soru yok.</param>
/// <param name="Development">Kod projeye uygulandıysa: değişiklikler, derleme ve test sonuçları.</param>
public sealed record PipelineResult(
    string EnglishRequest,
    string Analysis,
    string Code,
    string Tests,
    string Review,
    bool Approved,
    int ReviewRounds,
    string FinalMarkdown,
    IReadOnlyList<GeneratedFile> Files,
    ContextInfo Context,
    IReadOnlyDictionary<string, string> Models,
    double Seconds,
    AgentPlan Plan,
    string Questions = "",
    DevelopmentSummary? Development = null);

/// <summary>Kodun projeye uygulandığı akışın (ApplyChanges) sonucu.</summary>
/// <param name="ProjectPath">Kodun yazıldığı proje klasörü.</param>
/// <param name="ProjectCreated">Proje bu istekte şablondan oluşturuldu mu.</param>
/// <param name="Snapshot">Değişikliklerden önceki anlık görüntü; geri almak için kullanılır.</param>
/// <param name="BuildAttempts">Kaç kez derlendi (ilk derleme + düzeltmeler).</param>
/// <param name="TestAttempts">Testler kaç kez çalıştırıldı.</param>
/// <param name="Problem">Akış yarıda kaldıysa nedeni (örn. ajan dosya üretmedi).</param>
public sealed record DevelopmentSummary(
    string ProjectPath,
    string? Architecture,
    bool ProjectCreated,
    string? Snapshot,
    IReadOnlyList<FileChange> Changes,
    BuildResult? Build,
    int BuildAttempts,
    TestRunResult? Tests,
    int TestAttempts,
    string? Problem);
