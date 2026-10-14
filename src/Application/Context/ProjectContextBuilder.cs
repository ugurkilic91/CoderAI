using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Options;
using DevAgents.Domain.Common;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.Context;

/// <summary>
/// Her ajana verilen, mimariyi anlatan bağlamı kurar: proje özeti (framework, paketler, katmanlar, dosya ağacı)
/// ve karakter bütçesine sığan, isteğe en uygun kaynak dosyalar.
/// </summary>
internal sealed class ProjectContextBuilder(IWorkspaceFileSystem workspace, IOptionsMonitor<WorkspaceOptions> options)
    : IProjectContextBuilder
{
    public ProjectContext Build(string? projectPath, IEnumerable<InlineFile>? inlineFiles, string query, ContextBuildOptions? buildOptions = null)
    {
        var settings = options.CurrentValue;
        var filter = ProjectFileFilter.FromOptions(settings);
        var files = new Dictionary<string, ProjectFile>(StringComparer.OrdinalIgnoreCase);
        var projectName = "(inline files)";
        string? warning = null;

        // Proje klasörü verildiyse diskteki dosyaları indeksliyoruz (yol güvenliği dosya sistemi katmanında kontrol edilir).
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var root = workspace.ResolveProjectPath(projectPath);
            projectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var file in workspace.EnumerateFiles(root, filter))
                files[file.RelativePath] = file;
            if (files.Count >= settings.MaxFiles)
                warning = $"Dosya sayısı sınırına ulaşıldı ({settings.MaxFiles}); bazı dosyalar indekslenmedi.";
        }

        // İstemcinin gönderdiği dosyaları ekliyoruz; sabitlenenler daha sonra her zaman bağlama girecek.
        var pinned = AddInlineFiles(files, inlineFiles, filter);
        if (files.Count == 0)
            return ProjectContext.None;

        var overview = ProjectOverview.Build(projectName, files);
        var selection = ContextFileSelector.Select(files.Values, pinned, query,
            buildOptions?.MaxContextChars ?? settings.MaxContextChars, settings.MaxCharsPerFile, buildOptions?.ExcludedPaths);
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
    /// İstemcinin dosyalarını ekler (aynı yoldaki indekslenmiş dosyanın yerine geçer) ve sabitlenenleri döndürür.
    /// Sabitlenmemiş dosyalar, indekslenen dosyalarla aynı filtreden geçmek zorundadır.
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
