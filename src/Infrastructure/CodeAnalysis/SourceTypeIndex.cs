using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DevAgents.Infrastructure.CodeAnalysis;

/// <summary>Bir tipin adı ve tip parametresi sayısı: <c>Ok&lt;T&gt;</c> → ("Ok", 1).</summary>
internal readonly record struct TypeKey(string Name, int Arity);

/// <summary>Bir tanımın namespace'i ve tanımlandığı .csproj (proje dışındaki gevşek dosyalarda null).</summary>
internal readonly record struct Declaration(string Namespace, string? Project);

/// <summary>
/// Projenin kaynak kodundaki tipler ve extension metotları: hangisi hangi namespace'te, hangi .csproj'da tanımlı.
/// Derleme yapılmaz; dosyalar yalnızca Roslyn ile ayrıştırılır (yüzlerce dosya için milisaniyeler).
/// </summary>
internal sealed class SourceTypeIndex
{
    // Derleme çıktıları ve araç klasörleri taranmaz ("." ile başlayanlar da: .git, .vs ...).
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "artifacts", "node_modules" };

    private readonly string _root;
    private readonly Dictionary<TypeKey, List<Declaration>> _types = [];
    private readonly Dictionary<string, List<Declaration>> _extensionMethods = new(StringComparer.Ordinal);
    private readonly List<Declaration> _globalUsings = [];
    private readonly Dictionary<string, string?> _projectOfDirectory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _visibleProjects = new(StringComparer.Ordinal);

    private SourceTypeIndex(string root) => _root = root;

    /// <summary>Projedeki dosyaların using satırlarında geçen namespace'ler (birden çok aday olduğunda seçim için).</summary>
    public HashSet<string> UsedNamespaces { get; } = new(StringComparer.Ordinal);

    public static SourceTypeIndex Build(string projectRoot, CancellationToken cancellationToken)
    {
        var index = new SourceTypeIndex(Path.GetFullPath(projectRoot));
        foreach (var path in EnumerateSourceFiles(index._root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            var unit = CSharpSyntaxTree.ParseText(text, RoslynSyntaxChecker.ParseOptions, cancellationToken: cancellationToken)
                .GetCompilationUnitRoot(cancellationToken);
            index.Add(unit, index.ProjectOf(path));
        }
        return index;
    }

    /// <summary>Tipin tanımlandığı namespace'ler; yalnızca dosyanın görebildiği projelerde tanımlananlar.</summary>
    public IEnumerable<string> NamespacesOf(TypeKey key, string? project) => Visible(_types.GetValueOrDefault(key), project);

    /// <summary>Bu adda extension metodu tanımlayan namespace'ler; yalnızca görünür projelerde tanımlananlar.</summary>
    public IEnumerable<string> ExtensionNamespacesOf(string method, string? project) => Visible(_extensionMethods.GetValueOrDefault(method), project);

    /// <summary>Projenin kaynak dosyalarındaki "global using" satırları (yalnızca o projede geçerlidir).</summary>
    public IEnumerable<string> GlobalUsingsOf(string? project) =>
        _globalUsings.Where(declaration => declaration.Project == project).Select(declaration => declaration.Namespace);

    /// <summary>Dosyanın ait olduğu .csproj: dosyadan yukarı doğru ilk bulunan; proje kökünün dışına çıkılmaz.</summary>
    public string? ProjectOf(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var visited = new List<string>();
        string? project = null;
        while (directory is not null && IsUnder(directory, _root))
        {
            if (_projectOfDirectory.TryGetValue(directory, out project))
                break;
            visited.Add(directory);
            project = Directory.EnumerateFiles(directory, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault();
            if (project is not null)
                break;
            directory = Path.GetDirectoryName(directory);
        }
        foreach (var path in visited)
            _projectOfDirectory[path] = project;
        return project;
    }

    // Projenin kendisi ve (dolaylı olanlar dahil) ProjectReference ile başvurduğu projeler: yalnızca bunların tipleri görünür.
    private HashSet<string> VisibleProjects(string project)
    {
        if (_visibleProjects.TryGetValue(project, out var visible))
            return visible;

        visible = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([project]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visible.Add(current))
                continue;
            foreach (var reference in ProjectReferences(current))
                pending.Push(reference);
        }
        return _visibleProjects[project] = visible;
    }

    private IEnumerable<string> Visible(List<Declaration>? declarations, string? project) =>
        declarations is null
            ? []
            : declarations
                .Where(declaration => project is null || declaration.Project is null || VisibleProjects(project).Contains(declaration.Project))
                .Select(declaration => declaration.Namespace)
                .Distinct(StringComparer.Ordinal);

    private void Add(CompilationUnitSyntax unit, string? project)
    {
        foreach (var directive in unit.Usings)
        {
            if (NamespaceOf(directive) is not { } name)
                continue;
            UsedNamespaces.Add(name);
            if (directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
                _globalUsings.Add(new Declaration(name, project));
        }
        foreach (var member in unit.Members)
            AddMember(member, "", project);
    }

    private void AddMember(MemberDeclarationSyntax member, string containingNamespace, string? project)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax declaration:
                var name = containingNamespace.Length == 0 ? Normalize(declaration.Name) : $"{containingNamespace}.{Normalize(declaration.Name)}";
                foreach (var directive in declaration.Usings)
                {
                    if (NamespaceOf(directive) is { } used)
                        UsedNamespaces.Add(used);
                }
                foreach (var child in declaration.Members)
                    AddMember(child, name, project);
                break;

            // Global namespace'teki tipler zaten her yerden görünür; "file" tipleri dosya dışından görünmez.
            case BaseTypeDeclarationSyntax type when containingNamespace.Length > 0 && !type.Modifiers.Any(SyntaxKind.FileKeyword):
                var arity = (type as TypeDeclarationSyntax)?.TypeParameterList?.Parameters.Count ?? 0;
                Add(_types, new TypeKey(type.Identifier.ValueText, arity), new Declaration(containingNamespace, project));
                if (type is ClassDeclarationSyntax @class && @class.Modifiers.Any(SyntaxKind.StaticKeyword))
                {
                    foreach (var method in @class.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (method.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true)
                            Add(_extensionMethods, method.Identifier.ValueText, new Declaration(containingNamespace, project));
                    }
                }
                break;

            case DelegateDeclarationSyntax @delegate when containingNamespace.Length > 0:
                Add(_types, new TypeKey(@delegate.Identifier.ValueText, @delegate.TypeParameterList?.Parameters.Count ?? 0),
                    new Declaration(containingNamespace, project));
                break;
        }
    }

    /// <summary>"using X.Y;" satırının namespace'i; takma ad (using A = ...) ve "using static" satırlarında null.</summary>
    internal static string? NamespaceOf(UsingDirectiveSyntax directive) =>
        directive.Alias is null && !directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && directive.NamespaceOrType is { } name
            ? Normalize(name)
            : null;

    // "global::System.Linq" → "System.Linq"; ad içindeki boşluklar atılır.
    private static string Normalize(TypeSyntax name) =>
        string.Concat(name.ToString().Replace("global::", "", StringComparison.Ordinal).Where(c => !char.IsWhiteSpace(c)));

    private static void Add<TKey>(Dictionary<TKey, List<Declaration>> map, TKey key, Declaration declaration)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        if (!list.Contains(declaration))
            list.Add(declaration);
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return [];
        }
        var directory = Path.GetDirectoryName(project)!;
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include) && !include.Contains("$(", StringComparison.Ordinal))
            .Select(include => Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files, directories;
            try
            {
                files = Directory.GetFiles(directory, "*.cs");
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in files)
                yield return file;
            foreach (var child in directories)
            {
                var name = Path.GetFileName(child);
                if (!name.StartsWith('.') && !SkippedDirectories.Contains(name))
                    pending.Push(child);
            }
        }
    }

    internal static bool IsUnder(string path, string root) =>
        path == root || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
