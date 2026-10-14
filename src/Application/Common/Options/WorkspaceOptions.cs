namespace DevAgents.Application.Common.Options;

/// <summary>Hangi proje dosyalarının okunacağı ve ne kadarının ajanlara gideceği; "Workspace" bölümünden okunur.</summary>
public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    /// <summary>Boşsa makinedeki her yol kullanılabilir. Docker'da ["/workspace"] olarak ayarlanır.</summary>
    public string[] AllowedRoots { get; set; } = [];

    /// <summary>Bağlama giren dosyaların toplam karakter bütçesi.</summary>
    public int MaxContextChars { get; set; } = 24000;

    public int MaxCharsPerFile { get; set; } = 8000;

    /// <summary>Bundan büyük dosyalar indekslenmez.</summary>
    public long MaxFileBytes { get; set; } = 200_000;

    public int MaxFiles { get; set; } = 5000;

    /// <summary>İsteğe bağlı; boşsa yerleşik varsayılanlar kullanılır.</summary>
    public string[] IncludeExtensions { get; set; } = [];

    public string[] ExcludeDirectories { get; set; } = [];
}
