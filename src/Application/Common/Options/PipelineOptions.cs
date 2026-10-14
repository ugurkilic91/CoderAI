namespace DevAgents.Application.Common.Options;

/// <summary>Ajan akışının davranışı; yapılandırmadaki "Pipeline" bölümünden okunur.</summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Review "değişiklik gerekli" dediğinde kodlama ajanı en fazla kaç kez düzeltme yapabilir.</summary>
    public int MaxReviewIterations { get; set; } = 1;

    /// <summary>Önceki sohbet turlarından kaçı ajanlara verilir.</summary>
    public int MaxHistoryItems { get; set; } = 2;

    /// <summary>Son rapor Türkçeye çevrilsin mi.</summary>
    public bool TranslateOutput { get; set; } = true;

    /// <summary>
    /// true: istek zaten İngilizceyse çeviri ajanı çağrılmaz (bir model çağrısı kazanılır).
    /// false: her istek çevirmenden geçer.
    /// </summary>
    public bool DetectInputLanguage { get; set; } = true;
}
