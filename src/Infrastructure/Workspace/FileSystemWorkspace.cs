using System.Security;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Domain.Common;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Workspace;

/// <summary>Proje klasörlerini yerel diskten okur; erişim <see cref="WorkspaceOptions.AllowedRoots"/> ile sınırlıdır.</summary>
internal sealed class FileSystemWorkspace(IOptionsMonitor<WorkspaceOptions> options) : IWorkspaceFileSystem
{
    public string ResolveProjectPath(string path)
    {
        var roots = options.CurrentValue.AllowedRoots;
        string fullPath;
        try
        {
            var candidate = path.Trim().Trim('"');
            // Yalnızca klasör adı ("MyApp") verildiyse ilk izinli kökün altındaki proje kastediliyor.
            if (!Path.IsPathRooted(candidate) && roots.Length > 0)
                candidate = Path.Combine(roots[0], candidate);
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProjectPathException($"Geçersiz proje yolu: {path}");
        }

        // Güvenlik: "../" gibi yollarla izinli köklerin dışına çıkılamaz.
        if (roots.Length > 0 && !roots.Any(root => IsUnder(fullPath, Path.GetFullPath(root))))
            throw new ProjectPathException($"Yol izin verilen kök dizinlerin dışında: {fullPath}");
        if (!Directory.Exists(fullPath))
            throw new ProjectPathException($"Proje klasörü bulunamadı: {fullPath}");
        return fullPath;
    }

    public string ResolveNewProjectDirectory(string? parentPath, string projectName)
    {
        // Üst klasör: kullanıcının seçtiği klasör, yoksa ilk izinli kök (Docker'da /workspace).
        var roots = options.CurrentValue.AllowedRoots;
        var parent = !string.IsNullOrWhiteSpace(parentPath) ? ResolveProjectPath(parentPath)
            : roots.Length > 0 ? ResolveProjectPath(roots[0])
            : throw new ProjectPathException("Yeni projenin oluşturulacağı klasörü seçin (Proje klasörü alanı).");

        var target = Path.GetFullPath(Path.Combine(parent, projectName));
        if (!IsUnder(target, parent) || Path.GetFileName(target) != projectName)
            throw new ProjectPathException($"Geçersiz proje adı: {projectName}");
        if (Directory.Exists(target) || File.Exists(target))
            throw new ProjectPathException($"Bu adda bir klasör zaten var: {target}");
        return target;
    }

    public string? ReadFile(string root, string relativePath)
    {
        // Yalnızca proje kökünün altındaki dosyalar okunur.
        var normalized = RelativePath.Normalize(relativePath);
        if (normalized is null)
            return null;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsUnder(fullPath, Path.GetFullPath(root)) || !File.Exists(fullPath))
            return null;
        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (IsAccessError(ex))
        {
            return null;
        }
    }

    public IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter)
    {
        var files = new List<ProjectFile>();
        var pending = new Stack<string>();
        pending.Push(root);

        // Klasör ağacını derinlik öncelikli geziyoruz; dosya sayısı sınırına gelince duruyoruz.
        while (pending.Count > 0 && files.Count < filter.MaxFiles)
        {
            var directory = pending.Pop();
            string[] subdirectories, directoryFiles;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                directoryFiles = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (IsAccessError(ex))
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                if (!filter.IsExcludedDirectory(Path.GetFileName(subdirectory)))
                    pending.Push(subdirectory);
            }

            foreach (var file in directoryFiles)
            {
                if (files.Count >= filter.MaxFiles)
                    break;
                if (!filter.HasIncludedExtension(file))
                    continue;

                var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!filter.Accepts(relativePath))
                    continue;

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    continue;
                }
                if (size == 0 || size > filter.MaxFileBytes)
                    continue;

                files.Add(new ProjectFile(relativePath, size, () => ReadTextOrEmpty(file)));
            }
        }
        return files;
    }

    public IReadOnlyList<WorkspaceProject> ListProjects(IEnumerable<string> roots) =>
    [
        .. roots.Where(Directory.Exists)
            .SelectMany(root => Directory.GetDirectories(root).Select(directory => new WorkspaceProject(Path.GetFileName(directory), directory)))
            .OrderBy(project => project.Name)
    ];

    // İndekslendikten sonra silinen ya da okunamaz hâle gelen dosya bağlama hiçbir şey katmaz.
    private static string ReadTextOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (IsAccessError(ex))
        {
            return "";
        }
    }

    private static bool IsAccessError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;

    private static bool IsUnder(string path, string root)
    {
        var separator = Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (path.TrimEnd(separator) + separator).StartsWith(root.TrimEnd(separator) + separator, comparison);
    }
}
