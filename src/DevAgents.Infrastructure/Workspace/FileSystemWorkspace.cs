using System.Security;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Workspace;

/// <summary>Reads project folders from the local disk, restricted to <see cref="WorkspaceOptions.AllowedRoots"/>.</summary>
internal sealed class FileSystemWorkspace(IOptionsMonitor<WorkspaceOptions> options) : IWorkspaceFileSystem
{
    public string ResolveProjectPath(string path)
    {
        var roots = options.CurrentValue.AllowedRoots;
        string fullPath;
        try
        {
            var candidate = path.Trim().Trim('"');
            // A bare folder name ("MyApp") means a project under the first allowed root.
            if (!Path.IsPathRooted(candidate) && roots.Length > 0)
                candidate = Path.Combine(roots[0], candidate);
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidRequestException($"Geçersiz proje yolu: {path}");
        }

        if (roots.Length > 0 && !roots.Any(root => IsUnder(fullPath, Path.GetFullPath(root))))
            throw new InvalidRequestException($"Yol izin verilen kök dizinlerin dışında: {fullPath}");
        if (!Directory.Exists(fullPath))
            throw new InvalidRequestException($"Proje klasörü bulunamadı: {fullPath}");
        return fullPath;
    }

    public IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter)
    {
        var files = new List<ProjectFile>();
        var pending = new Stack<string>();
        pending.Push(root);

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

    // A file that disappears or becomes unreadable after indexing simply contributes nothing.
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
