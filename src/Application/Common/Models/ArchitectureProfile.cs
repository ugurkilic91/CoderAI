namespace DevAgents.Application.Common.Models;

/// <summary>
/// Ajanların kod üretirken uyacağı mimari: kurallar, örnek dosyalar, yeni proje oluşturma ve test komutları.
/// architectures/&lt;id&gt;/ klasöründen okunur; şirket şablonu eklemek için yeni bir klasör eklemek yeterlidir.
/// </summary>
/// <param name="Rules">rules.md: ajanların izleyeceği kurallar (İngilizce; modeller İngilizcede daha iyi uyuyor).</param>
/// <param name="TemplateDirectory">Şablonun kaynak klasörü (dotnet new install için); yoksa bu mimariyle yeni proje açılamaz.</param>
/// <param name="SourceName">Şablonun örneklerde geçen adı (örn. "CleanArchitecture"); ajanlara proje adıyla değiştirilerek verilir.</param>
/// <param name="NewProjectArguments">Yeni proje için dotnet argümanları; "{name}" proje adıyla değiştirilir.</param>
/// <param name="TestProjects">Çalıştırılacak test projeleri (proje köküne göre göreli).</param>
/// <param name="ExampleFiles">Projeden (yoksa şablondan) okunup ajanlara örnek olarak verilen dosyalar.</param>
/// <param name="ExtraExamples">Profil klasöründeki ek örnekler (şablonda karşılığı olmayan kalıplar, örn. handler testi).</param>
public sealed record ArchitectureProfile(
    string Id,
    string Name,
    string Description,
    string Rules,
    string? TemplateDirectory,
    string? SourceName,
    IReadOnlyList<string> NewProjectArguments,
    IReadOnlyList<string> TestProjects,
    IReadOnlyList<string> ExampleFiles,
    IReadOnlyList<ArchitectureExample> ExtraExamples)
{
    /// <summary>Bu mimariyle yeni proje oluşturulabilir mi (şablon klasörü ve komut tanımlı mı).</summary>
    public bool CanCreateProjects() => TemplateDirectory is not null && NewProjectArguments.Count > 0;
}

/// <summary>Profil klasöründen gelen örnek dosya.</summary>
public sealed record ArchitectureExample(string Path, string Content);
