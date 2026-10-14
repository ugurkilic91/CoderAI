using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>
/// "Tip bulunamadı" derleme hatalarını model kullanmadan düzeltir (IDE'deki "eksik using'i ekle" gibi):
/// tipin hangi namespace'te olduğunu projenin kaynak kodundan ve projenin başvurduğu paketlerden bulur.
/// </summary>
public interface IUsingDirectiveFixer
{
    /// <summary>
    /// Hatalardaki eksik tipler için dosyalara using satırı ekler ve dosyaların yeni içeriğini döndürür (diske yazmaz).
    /// Tip tek bir namespace'te bulunamazsa (hiç yoksa ya da birden çok aday varsa) o hataya dokunmaz; onu düzeltme ajanı çözer.
    /// </summary>
    Task<IReadOnlyList<UsingFix>> FixAsync(string projectRoot, IReadOnlyList<CompilerDiagnostic> errors, CancellationToken cancellationToken);
}
