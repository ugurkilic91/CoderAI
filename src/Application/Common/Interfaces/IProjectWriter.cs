using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Ajanların ürettiği dosyaları proje klasörüne yazar.</summary>
public interface IProjectWriter
{
    /// <summary>
    /// Dosyaları <paramref name="projectRoot"/> altına yazar ve yapılan değişiklikleri döndürür (içeriği aynı kalanlar dahil edilmez).
    /// Proje dışına, .git/bin/obj klasörlerine yazmaya çalışan dosyalar atlanır.
    /// </summary>
    Task<IReadOnlyList<FileChange>> WriteAsync(string projectRoot, IReadOnlyList<GeneratedFile> files, CancellationToken cancellationToken);
}
