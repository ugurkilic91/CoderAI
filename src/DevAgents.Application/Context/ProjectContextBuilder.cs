using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Options;
using DevAgents.Domain.Files;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.Context;

/// <summary>
/// Builds the architecture-aware context handed to every agent: a project overview (frameworks, packages,
/// layers, file tree) plus the most relevant source files within a character budget.
/// </summary>
internal sealed class ProjectContextBuilder(IWorkspaceFileSystem workspace, IOptionsMonitor<WorkspaceOptions> options)
    : IProjectContextBuilder
{
    public ProjectContext Build(string? projectPath, IEnumerable<InlineFile>? inlineFiles, string query)
    {
        var settings = options.CurrentValue;
        var filter = ProjectFileFilter.FromOptions(settings);
        var files = new Dictionary<string, ProjectFile>(StringComparer.OrdinalIgnoreCase);
        var projectName = "(inline files)";
        string? warning = null;

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var root = workspace.ResolveProjectPath(projectPath);
            projectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var file in workspace.EnumerateFiles(root, filter))
                files[file.RelativePath] = file;
            if (files.Count >= settings.MaxFiles)
                warning = $"Dosya sayısı sınırına ulaşıldı ({settings.MaxFiles}); bazı dosyalar indekslenmedi.";
        }

        var pinned = AddInlineFiles(files, inlineFiles, filter);
        if (files.Count == 0)
            return ProjectContext.None;

        var overview = ProjectOverview.Build(projectName, files);
        var selection = ContextFileSelector.Select(files.Values, pinned, query, settings);
        return new ProjectContext
        {
            Overview = overview,
            FilesText = selection.Text,
            Included = selection.Included,
            TotalFiles = files.Count,
            Warning = warning
        };
    }

    /// <summary>
    /// Adds the client's files (they replace indexed files with the same path) and returns the pinned ones.
    /// Files that are not pinned must pass the same filter as indexed files.
    /// </summary>
    private static List<ProjectFile> AddInlineFiles(
        Dictionary<string, ProjectFile> files, IEnumerable<InlineFile>? inlineFiles, ProjectFileFilter filter)
    {
        var pinned = new List<ProjectFile>();
        if (inlineFiles is null)
            return pinned;

        foreach (var inline in inlineFiles)
        {
            var path = RelativePath.Normalize(inline?.Path);
            if (inline is null || path is null || string.IsNullOrEmpty(inline.Content))
                continue;
            if (!inline.Pinned && !filter.Accepts(path))
                continue;

            var file = ProjectFile.FromContent(path, inline.Content);
            files[path] = file;
            if (inline.Pinned)
                pinned.Add(file);
        }
        return pinned;
    }
}
