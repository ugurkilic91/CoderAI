using DevAgents.Application.Common.Models;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Kayıtlı mimari profilleri (architectures/ klasörü).</summary>
public interface IArchitectureCatalog
{
    IReadOnlyList<ArchitectureProfile> GetAll();

    /// <summary>Kimliğe göre profil; yoksa <c>null</c>.</summary>
    ArchitectureProfile? Find(string id);
}
