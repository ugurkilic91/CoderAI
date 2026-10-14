using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Context;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Ajanların bakabileceği proje klasörlerine erişim.</summary>
public interface IWorkspaceFileSystem
{
    /// <summary>
    /// Kullanıcının yazdığı yolu (mutlak yol ya da ilk izinli kök altındaki klasör adı) tam yola çevirir.
    /// </summary>
    /// <exception cref="ProjectPathException">Yol izinli köklerin dışında, geçersiz ya da klasör yok.</exception>
    string ResolveProjectPath(string path);

    /// <summary>
    /// Yeni projenin oluşturulacağı klasörü belirler: <paramref name="parentPath"/> (boşsa ilk izinli kök) altında
    /// <paramref name="projectName"/> adlı, henüz var olmayan bir klasör.
    /// </summary>
    /// <exception cref="ProjectPathException">Üst klasör kullanılamıyor ya da bu adda bir klasör zaten var.</exception>
    string ResolveNewProjectDirectory(string? parentPath, string projectName);

    /// <summary>
    /// <paramref name="root"/> altındaki, <paramref name="filter"/>'dan geçen dosyaları listeler.
    /// Listelendikten sonra okunamaz hâle gelen bir dosyanın içeriği boş döner.
    /// </summary>
    IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter);

    /// <summary>Projedeki bir dosyanın güncel içeriği; dosya yoksa ya da proje dışındaysa <c>null</c>.</summary>
    string? ReadFile(string root, string relativePath);

    /// <summary><paramref name="roots"/> altındaki klasörler, yani kullanıcının seçebileceği projeler.</summary>
    IReadOnlyList<WorkspaceProject> ListProjects(IEnumerable<string> roots);
}
