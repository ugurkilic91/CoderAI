using System.Text;
using System.Text.RegularExpressions;

namespace DevAgents.Application.Context;

/// <summary>Projeyi ajanlar için özetler: .NET projeleri (framework, paketler, referanslar), olası katmanlar ve dosya ağacı.</summary>
internal static partial class ProjectOverview
{
    private const int MaxProjects = 30;
    private const int MaxPackagesPerProject = 15;
    private const int MaxTreeEntries = 300;

    // Mimariyi ele veren klasör adları (katmanlar, CQRS, ...).
    private static readonly string[] LayerHints =
    [
        "Controllers", "Services", "Repositories", "Domain", "Entities", "Models", "Dtos", "Application",
        "Infrastructure", "Handlers", "Commands", "Queries", "Validators", "Mappers", "Profiles", "Middleware",
        "Migrations", "Extensions", "Interfaces", "Contracts", "Features", "Endpoints", "Data", "Persistence", "Tests"
    ];

    public static string Build(string projectName, IReadOnlyDictionary<string, ProjectFile> files)
    {
        var overview = new StringBuilder();
        overview.AppendLine($"Project: {projectName} ({files.Count} indexed files)");
        AppendDotNetProjects(overview, files.Values);
        AppendLayers(overview, files.Keys);
        AppendFileTree(overview, files.Keys);
        return overview.ToString().TrimEnd();
    }

    // .csproj dosyalarından hedef framework, paketler ve proje referansları.
    private static void AppendDotNetProjects(StringBuilder overview, IEnumerable<ProjectFile> files)
    {
        var projects = files
            .Where(file => file.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath)
            .Take(MaxProjects)
            .ToList();
        if (projects.Count == 0)
            return;

        overview.AppendLine("\n.NET projects:");
        foreach (var project in projects)
        {
            var xml = project.ReadContent();
            var framework = TargetFramework().Match(xml).Groups[1].Value;
            var packages = PackageReference().Matches(xml).Select(match => match.Groups[1].Value).Take(MaxPackagesPerProject);
            var references = ProjectReference().Matches(xml)
                .Select(match => Path.GetFileNameWithoutExtension(match.Groups[1].Value.Replace('\\', '/')));
            overview.AppendLine(
                $"- {project.RelativePath}: {(framework.Length > 0 ? framework : "?")}; " +
                $"packages: [{string.Join(", ", packages)}]; references: [{string.Join(", ", references)}]");
        }
    }

    private static void AppendLayers(StringBuilder overview, IEnumerable<string> paths)
    {
        var directoryNames = paths
            .SelectMany(path => path.Split('/').SkipLast(1))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var layers = LayerHints.Where(directoryNames.Contains).ToList();
        if (layers.Count > 0)
            overview.AppendLine($"\nFolder names suggest these layers/patterns: {string.Join(", ", layers)}");
    }

    // Önce sığ dosyalar: ajan proje yapısını üstten görsün.
    private static void AppendFileTree(StringBuilder overview, IEnumerable<string> paths)
    {
        overview.AppendLine("\nFile tree (shallow first):");
        var ordered = paths
            .OrderBy(path => path.Count(c => c == '/'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var path in ordered.Take(MaxTreeEntries))
            overview.AppendLine("  " + path);
        if (ordered.Count > MaxTreeEntries)
            overview.AppendLine($"  ... (+{ordered.Count - MaxTreeEntries} more)");
    }

    [GeneratedRegex("<TargetFrameworks?>([^<]+)<")]
    private static partial Regex TargetFramework();

    [GeneratedRegex("<PackageReference\\s+Include=\"([^\"]+)\"")]
    private static partial Regex PackageReference();

    [GeneratedRegex("<ProjectReference\\s+Include=\"([^\"]+)\"")]
    private static partial Regex ProjectReference();
}
