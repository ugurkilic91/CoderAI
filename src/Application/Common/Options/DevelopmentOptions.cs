namespace DevAgents.Application.Common.Options;

/// <summary>Kodun projeye uygulanıp derlendiği/test edildiği akışın ayarları; "Development" bölümünden okunur.</summary>
public sealed class DevelopmentOptions
{
    public const string SectionName = "Development";

    /// <summary>Derleme hataları için en fazla kaç düzeltme turu yapılır.</summary>
    public int MaxBuildFixAttempts { get; set; } = 3;

    /// <summary>Başarısız testler için en fazla kaç düzeltme turu yapılır.</summary>
    public int MaxTestFixAttempts { get; set; } = 2;

    /// <summary>
    /// Uygulama modunda proje dosyaları için karakter bütçesi. Mimari kuralları ve örnekleri ayrıca eklendiği için
    /// normal bütçeden küçük tutulur; modelin bağlam penceresi (NumCtx) taşmasın.
    /// </summary>
    public int MaxContextChars { get; set; } = 14000;

    /// <summary>Düzeltme ajanına gönderilecek en fazla hata sayısı.</summary>
    public int MaxErrorsPerFix { get; set; } = 20;
}
