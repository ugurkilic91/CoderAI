using System.Text;
using System.Text.RegularExpressions;
using DevAgents.Models;
using DevAgents.Options;
using Microsoft.Extensions.Options;

namespace DevAgents.Services;

public sealed record ProjectFile(string RelPath, long Size, Func<string> Read);

public sealed class ProjectContext
{
    public string Overview { get; init; } = "";
    public string FilesText { get; init; } = "";
    public List<string> Included { get; init; } = [];
    public int TotalFiles { get; init; }
    public string? Warning { get; init; }

    public string Text => $"### PROJECT OVERVIEW\n{Overview}\n\n### RELEVANT PROJECT FILES\n{(FilesText.Length == 0 ? "(none)" : FilesText)}";
    public int Chars => Text.Length;
}

/// <summary>
/// Builds the architecture-aware context that is handed to every agent:
/// project overview (frameworks, packages, layers, file tree) + the most relevant source files within a character budget.
/// </summary>
public sealed class ProjectContextBuilder(IOptionsMonitor<WorkspaceOptions> opts)
{
    private static readonly string[] DefaultExt =
    [
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".json", ".md", ".cshtml", ".razor", ".sql",
        ".yml", ".yaml", ".xml", ".config", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".html", ".css", ".proto"
    ];

    private static readonly string[] DefaultExcludeDirs =
    [
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".vscode", "packages", "dist", "out",
        "TestResults", ".next", "coverage", "__pycache__", ".venv", "venv"
    ];

    private static readonly string[] LayerHints =
    [
        "Controllers", "Services", "Repositories", "Domain", "Entities", "Models", "Dtos", "Application",
        "Infrastructure", "Handlers", "Commands", "Queries", "Validators", "Mappers", "Profiles", "Middleware",
        "Migrations", "Extensions", "Interfaces", "Contracts", "Features", "Endpoints", "Data", "Persistence", "Tests"
    ];

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","that","this","from","into","using","use","add","new","get","set","create","make",
        "need","want","should","would","could","please","class","method","code","file","files","project","implement",
        "write","build","test","tests","service","controller","have","has","are","was","will","can","all","any",
        "each","when","then","than","also","like","some","only","just","how","what","which","where","not","but"
    };

    // ---------- public API ----------

    public string ResolveProjectPath(string input)
    {
        var o = opts.CurrentValue;
        var p = input.Trim().Trim('"');
        if (!Path.IsPathRooted(p) && o.AllowedRoots.Length > 0)
            p = Path.Combine(o.AllowedRoots[0], p);

        var full = Path.GetFullPath(p);
        if (o.AllowedRoots.Length > 0 && !o.AllowedRoots.Any(r => IsUnder(full, Path.GetFullPath(r))))
            throw new InvalidOperationException($"Yol izin verilen kök dizinlerin dışında: {full}");
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Proje klasörü bulunamadı: {full}");
        return full;
    }

    public ProjectContext Build(string? projectPath, IEnumerable<InlineFile>? inline, string queryText)
    {
        var o = opts.CurrentValue;
        var exts = (o.IncludeExtensions.Length > 0 ? o.IncludeExtensions : DefaultExt)
            .Select(e => e.StartsWith('.') ? e : "." + e).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excl = (o.ExcludeDirectories.Length > 0 ? o.ExcludeDirectories : DefaultExcludeDirs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, ProjectFile>(StringComparer.OrdinalIgnoreCase);
        var rootName = "(inline files)";
        string? warning = null;

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var root = ResolveProjectPath(projectPath);
            rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (var f in EnumerateDisk(root, o, exts, excl)) map[f.RelPath] = f;
            if (map.Count >= o.MaxFiles) warning = $"Dosya sayısı sınırına ulaşıldı ({o.MaxFiles}); bazı dosyalar indekslenmedi.";
        }

        var pinned = new List<ProjectFile>();
        if (inline != null)
        {
            foreach (var f in inline)
            {
                var rel = MarkdownUtil.CleanPath(f.Path);
                if (rel == null || string.IsNullOrEmpty(f.Content)) continue;
                if (!f.Pinned && (!exts.Contains(Path.GetExtension(rel)) || IsSecretName(rel))) continue;
                var content = f.Content;
                var pf = new ProjectFile(rel, content.Length, () => content);
                map[rel] = pf;
                if (f.Pinned) pinned.Add(pf);
            }
        }

        if (map.Count == 0)
        {
            return new ProjectContext
            {
                Overview = "(no project context was provided)",
                TotalFiles = 0,
                Warning = "Proje yolu veya dosya verilmedi; agent'lar genel bilgiyle çalışacak."
            };
        }

        var overview = BuildOverview(rootName, map);
        var (filesText, included) = SelectFiles(map, pinned, queryText, o);
        return new ProjectContext
        {
            Overview = overview,
            FilesText = filesText,
            Included = included,
            TotalFiles = map.Count,
            Warning = warning
        };
    }

    // ---------- scanning ----------

    private static List<ProjectFile> EnumerateDisk(string root, WorkspaceOptions o, HashSet<string> exts, HashSet<string> excl)
    {
        var list = new List<ProjectFile>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && list.Count < o.MaxFiles)
        {
            var dir = stack.Pop();
            string[] subdirs, files;
            try { subdirs = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var d in subdirs)
                if (!excl.Contains(Path.GetFileName(d))) stack.Push(d);

            foreach (var f in files)
            {
                if (list.Count >= o.MaxFiles) break;
                if (!exts.Contains(Path.GetExtension(f))) continue;
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (IsSecretName(rel)) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len > o.MaxFileBytes || len == 0) continue;
                var path = f;
                list.Add(new ProjectFile(rel, len, () => File.ReadAllText(path)));
            }
        }
        return list;
    }

    private static bool IsSecretName(string rel)
    {
        var n = Path.GetFileName(rel).ToLowerInvariant();
        return n.Contains("secret") || n.StartsWith(".env") || n.EndsWith(".pfx") || n.EndsWith(".pem") ||
               n.EndsWith(".key") || n.Contains("appsettings.production");
    }

    // ---------- overview ----------

    private static string BuildOverview(string rootName, Dictionary<string, ProjectFile> map)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {rootName} ({map.Count} indexed files)");

        var csprojs = map.Values.Where(f => f.RelPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelPath).Take(30).ToList();
        if (csprojs.Count > 0)
        {
            sb.AppendLine("\n.NET projects:");
            foreach (var f in csprojs)
            {
                var txt = Safe(f.Read);
                var tfm = Regex.Match(txt, @"<TargetFrameworks?>([^<]+)<").Groups[1].Value;
                var pkgs = Regex.Matches(txt, @"<PackageReference\s+Include=""([^""]+)""").Select(m => m.Groups[1].Value).Take(15);
                var refs = Regex.Matches(txt, @"<ProjectReference\s+Include=""([^""]+)""")
                    .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/')));
                sb.AppendLine($"- {f.RelPath}: {(tfm.Length > 0 ? tfm : "?")}; packages: [{string.Join(", ", pkgs)}]; references: [{string.Join(", ", refs)}]");
            }
        }

        var dirNames = map.Keys.SelectMany(k => k.Split('/').SkipLast(1)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var layers = LayerHints.Where(dirNames.Contains).ToList();
        if (layers.Count > 0)
            sb.AppendLine($"\nFolder names suggest these layers/patterns: {string.Join(", ", layers)}");

        sb.AppendLine("\nFile tree (shallow first):");
        var paths = map.Keys.OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var p in paths.Take(300)) sb.AppendLine("  " + p);
        if (paths.Count > 300) sb.AppendLine($"  ... (+{paths.Count - 300} more)");
        return sb.ToString().TrimEnd();
    }

    // ---------- file selection ----------

    private static (string Text, List<string> Included) SelectFiles(
        Dictionary<string, ProjectFile> map, List<ProjectFile> pinned, string query, WorkspaceOptions o)
    {
        var sb = new StringBuilder();
        var included = new List<string>();
        var budget = o.MaxContextChars;

        bool Add(ProjectFile f, int cap, bool force = false)
        {
            if (included.Contains(f.RelPath)) return false;
            var text = Safe(f.Read);
            if (text.Length == 0) return false;
            var chunk = $"<file path=\"{f.RelPath}\">\n{MarkdownUtil.Truncate(text, cap)}\n</file>\n";
            if (!force && chunk.Length > budget) return false;
            budget -= chunk.Length;
            sb.Append(chunk);
            included.Add(f.RelPath);
            return true;
        }

        // 1) files the user explicitly pinned (attachments, active editor, selection)
        foreach (var f in pinned) Add(f, 12000, force: true);

        // 2) anchors: csproj, Program/Startup, README
        var rest = map.Values.Where(f => !pinned.Contains(f)).ToList();
        foreach (var f in rest.Where(f => f.RelPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.RelPath.Count(c => c == '/')).Take(3)) Add(f, 2500);
        foreach (var f in rest.Where(f => Path.GetFileName(f.RelPath) is "Program.cs" or "Startup.cs")
                     .OrderBy(f => f.RelPath.Count(c => c == '/')).Take(2)) Add(f, 4000);
        foreach (var f in rest.Where(f => Path.GetFileName(f.RelPath).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                     .Take(1)) Add(f, 1500);

        // 3) relevance by keyword overlap with the request (path match weighs more than content match)
        var tokens = Tokenize(query);
        var scored = new List<(ProjectFile F, int Score)>();
        foreach (var f in rest)
        {
            var lowerPath = f.RelPath.ToLowerInvariant();
            var score = 0;
            foreach (var t in tokens) if (lowerPath.Contains(t)) score += 6;
            if (f.Size <= 60_000 && IsCodeLike(f.RelPath))
            {
                var lower = Safe(f.Read).ToLowerInvariant();
                foreach (var t in tokens) score += Math.Min(CountOccurrences(lower, t), 5);
            }
            if (score > 0) scored.Add((f, score));
        }

        var perDir = new Dictionary<string, int>();
        foreach (var (f, _) in scored.OrderByDescending(x => x.Score).ThenBy(x => x.F.Size))
        {
            var dir = DirOf(f.RelPath);
            perDir.TryGetValue(dir, out var n);
            if (n >= 3) continue;
            if (Add(f, o.MaxCharsPerFile)) perDir[dir] = n + 1;
        }

        // 4) exemplars: one small source file from each folder not represented yet, to convey conventions
        var represented = included.Select(DirOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exemplarBudgetFloor = o.MaxContextChars / 5;
        var exemplars = 0;
        foreach (var g in rest.Where(f => f.RelPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && f.Size > 200)
                     .GroupBy(f => DirOf(f.RelPath))
                     .OrderBy(g => g.Key.Count(c => c == '/')).ThenBy(g => g.Key))
        {
            if (exemplars >= 8 || budget < exemplarBudgetFloor) break;
            if (represented.Contains(g.Key)) continue;
            if (Add(g.OrderBy(f => f.Size).First(), o.MaxCharsPerFile)) exemplars++;
        }

        return (sb.ToString(), included);
    }

    // ---------- helpers ----------

    private static string DirOf(string rel)
    {
        var i = rel.LastIndexOf('/');
        return i < 0 ? "" : rel[..i];
    }

    private static bool IsCodeLike(string rel) =>
        Path.GetExtension(rel).ToLowerInvariant() is ".cs" or ".cshtml" or ".razor" or ".ts" or ".tsx" or ".js"
            or ".jsx" or ".py" or ".java" or ".go" or ".sql" or ".md" or ".proto";

    private static List<string> Tokenize(string text)
    {
        var set = new HashSet<string>();
        foreach (Match m in Regex.Matches(text ?? "", @"[A-Za-z][A-Za-z0-9]{2,}"))
        {
            var word = m.Value;
            AddToken(set, word);
            // split camelCase / PascalCase: OrderService -> order, service
            foreach (Match p in Regex.Matches(word, @"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])"))
                AddToken(set, p.Value);
        }
        return set.Take(40).ToList();
    }

    private static void AddToken(HashSet<string> set, string w)
    {
        w = w.ToLowerInvariant();
        if (w.Length >= 3 && !Stop.Contains(w)) set.Add(w);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0; var idx = 0;
        while (count < 5 && (idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { count++; idx += needle.Length; }
        return count;
    }

    private static bool IsUnder(string path, string root)
    {
        var sep = Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (path.TrimEnd(sep) + sep).StartsWith(root.TrimEnd(sep) + sep, cmp);
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); } catch { return ""; }
    }
}
