using DevAgents.Application.Common.Exceptions;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>
/// Projedeki değişiklikleri izler ve geri almayı sağlar. Kayıtlar projenin dışında, gizli bir git deposunda tutulur;
/// projenin kendi git deposuna (varsa) dokunulmaz.
/// </summary>
public interface IChangeTracker
{
    /// <summary>Projenin o anki hâlinin anlık görüntüsünü alır ve kimliğini döndürür.</summary>
    /// <exception cref="ChangeTrackingException">Git bulunamadı ya da anlık görüntü alınamadı.</exception>
    Task<string> SnapshotAsync(string projectRoot, string message, CancellationToken cancellationToken);

    /// <summary><paramref name="snapshot"/>'tan bu yana projede değişen dosyalar.</summary>
    Task<IReadOnlyList<FileChange>> GetChangesAsync(string projectRoot, string snapshot, CancellationToken cancellationToken);

    /// <summary>Projeyi <paramref name="snapshot"/> anına döndürür (sonradan eklenen dosyaları siler) ve geri alınan değişiklikleri döndürür.</summary>
    Task<IReadOnlyList<FileChange>> RevertAsync(string projectRoot, string snapshot, CancellationToken cancellationToken);
}
