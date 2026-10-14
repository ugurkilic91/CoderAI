using DevAgents.Application.Common.Models;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>
/// Kodlama ajanı mevcut bir dosyayı değiştirirken üyelerini yanlışlıkla silebiliyor (örn. IApplicationDbContext'ten
/// diğer DbSet'ler). Derleme hatası hangi üyenin arandığını söyler; bu port o üyeyi dosyanın eski hâlinden geri ekler.
/// </summary>
public interface ICodeMemberRestorer
{
    /// <summary>
    /// <paramref name="current"/> içinde eksik olan ve <paramref name="original"/> içinde bulunan üyeleri geri ekler.
    /// Geri eklenecek bir üye yoksa null döner.
    /// </summary>
    MemberRestore? Restore(string original, string current, IReadOnlyCollection<MissingMember> missing);
}
