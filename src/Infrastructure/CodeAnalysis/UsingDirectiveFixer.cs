using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.Sandbox;
using DevAgents.Infrastructure.Storage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.CodeAnalysis;

/// <summary>
/// Eksik using satırlarını model kullanmadan ekler (IDE'deki "using ekle" hızlı düzeltmesi gibi).
/// Hatanın konumundaki ad Roslyn ile bulunur ve namespace'i iki yerde aranır:
/// 1) projenin kaynak kodu (örn. IApplicationDbContext → HobbyTracker.Application.Common.Interfaces),
/// 2) dosyanın projesinin başvurduğu paketler ve .NET çerçeveleri (örn. Ok&lt;T&gt; → Microsoft.AspNetCore.Http.HttpResults).
/// Ad tek bir namespace'te bulunursa using eklenir; bulunamazsa ya da adaylar birden çoksa dokunulmaz (düzeltme ajanı çözer).
/// </summary>
internal sealed partial class UsingDirectiveFixer(IOptionsMonitor<StorageOptions> storage, ILogger<UsingDirectiveFixer> logger)
    : IUsingDirectiveFixer
{
    // CS0246: tip bulunamadı, CS0103: ad bulunamadı (örn. Guard.Against), CS0308: generic olmayan tip tip argümanıyla kullanıldı
    // (örn. Results<,> yerine Microsoft.AspNetCore.Http.Results), CS1061: üye yok (extension metodu için using eksik olabilir).
    private static readonly HashSet<string> FixableCodes = new(StringComparer.Ordinal) { "CS0246", "CS0103", "CS0308", "CS1061" };

    public Task<IReadOnlyList<UsingFix>> FixAsync(string projectRoot, IReadOnlyList<CompilerDiagnostic> errors, CancellationToken cancellationToken)
    {
        var errorsByFile = errors
            .Where(error => FixableCodes.Contains(error.Code) && error.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .GroupBy(error => error.File, StringComparer.Ordinal)
            .ToList();
        if (errorsByFile.Count == 0)
            return Task.FromResult<IReadOnlyList<UsingFix>>([]);

        // Dosya okuma ve ayrıştırma senkron; isteği yürüten iş parçacığını bekletmemek için arka planda çalıştırıyoruz.
        return Task.Run(() => Fix(Path.GetFullPath(projectRoot), errorsByFile, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<UsingFix> Fix(string projectRoot, List<IGrouping<string, CompilerDiagnostic>> errorsByFile, CancellationToken cancellationToken)
    {
        try
        {
            var source = SourceTypeIndex.Build(projectRoot, cancellationToken);
            var projects = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
            var fixes = new List<UsingFix>();
            foreach (var fileErrors in errorsByFile)
            {
                var path = Path.GetFullPath(Path.Combine(projectRoot, fileErrors.Key));
                if (!SourceTypeIndex.IsUnder(path, projectRoot) || !File.Exists(path))
                    continue;

                var project = source.ProjectOf(path);
                var info = project is null ? ProjectInfo.None : projects.TryGetValue(project, out var cached) ? cached
                    : projects[project] = ProjectInfo.Load(project, projectRoot, ToLocalPath);
                if (FixFile(path, fileErrors.Key, fileErrors, project, info, source, cancellationToken) is { } fix)
                    fixes.Add(fix);
            }
            if (fixes.Count > 0)
                LogAdded(logger, fixes.Sum(fix => fix.AddedNamespaces.Count), fixes.Count);
            return fixes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Otomatik düzeltme bir kolaylık: yapılamazsa hatalar olduğu gibi düzeltme ajanına gider.
            LogFailed(logger, ex, projectRoot);
            return [];
        }
    }

    private static UsingFix? FixFile(string path, string relativePath, IEnumerable<CompilerDiagnostic> errors, string? project,
        ProjectInfo info, SourceTypeIndex source, CancellationToken cancellationToken)
    {
        var content = File.ReadAllText(path);
        var text = SourceText.From(content);
        var unit = CSharpSyntaxTree.ParseText(text, RoslynSyntaxChecker.ParseOptions, path, cancellationToken).GetCompilationUnitRoot(cancellationToken);
        var visible = VisibleNamespaces(unit, project, info, source);

        var added = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            if (NameAt(unit, text, error) is not { } name)
                continue;
            var candidates = Candidates(error.Code, name, project, info, source).Distinct(StringComparer.Ordinal).ToList();
            // Ad görünür bir namespace'te zaten varsa sorun using değildir (örn. proje başvurusu eksik): dokunmuyoruz.
            if (candidates.Count == 0 || candidates.Any(visible.Contains))
                continue;
            // Birden çok aday varsa projede zaten kullanılan namespace'i seçiyoruz (örn. NUnit.Framework ↔ NUnit.Framework.Internal).
            if (candidates.Count > 1)
                candidates = [.. candidates.Where(source.UsedNamespaces.Contains)];
            if (candidates.Count == 1)
                added.Add(candidates[0]);
        }
        return added.Count == 0 ? null : new UsingFix(relativePath, InsertUsings(content, unit, added), [.. added]);
    }

    /// <summary>Hatanın satır/sütununa denk gelen ad (Roslyn konumu; mesaj metnine bakmıyoruz, yerelleştirilmiş olabilir).</summary>
    private static SimpleNameSyntax? NameAt(CompilationUnitSyntax unit, SourceText text, CompilerDiagnostic error)
    {
        if (error.Line < 1 || error.Line > text.Lines.Count)
            return null;
        var line = text.Lines[error.Line - 1];
        var token = unit.FindToken(line.Start + Math.Clamp(error.Column - 1, 0, line.Span.Length));
        // using satırındaki hatalar (yanlış namespace) using eklenerek düzelmez.
        return token.IsKind(SyntaxKind.IdentifierToken) && token.Parent is SimpleNameSyntax name && name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is null
            ? name
            : null;
    }

    private static IEnumerable<string> Candidates(string code, SimpleNameSyntax name, string? project, ProjectInfo info, SourceTypeIndex source)
    {
        var identifier = name.Identifier.ValueText;
        if (code == "CS1061")
        {
            // Yalnızca "x.Metot(...)" biçimindeki üye erişimi: extension metodunun namespace'i.
            return name.Parent is MemberAccessExpressionSyntax access && access.Name == name
                ? source.ExtensionNamespacesOf(identifier, project).Concat(info.References.ExtensionNamespacesOf(identifier))
                : [];
        }

        var arity = name is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments.Count : 0;
        IEnumerable<string> Lookup(string typeName) =>
            source.NamespacesOf(new TypeKey(typeName, arity), project).Concat(info.References.NamespacesOf(new TypeKey(typeName, arity)));

        // [Test] → önce TestAttribute aranır (C# öznitelik adlarını böyle çözer).
        if (name.Parent is AttributeSyntax && !identifier.EndsWith("Attribute", StringComparison.Ordinal))
        {
            var attribute = Lookup(identifier + "Attribute").ToList();
            if (attribute.Count > 0)
                return attribute;
        }
        return Lookup(identifier);
    }

    /// <summary>Dosyada zaten görünen namespace'ler: kendi using'leri, projenin global using'leri, kendi namespace'i ve üstleri.</summary>
    private static HashSet<string> VisibleNamespaces(CompilationUnitSyntax unit, string? project, ProjectInfo info, SourceTypeIndex source)
    {
        var visible = new HashSet<string>(info.GeneratedGlobalUsings, StringComparer.Ordinal);
        visible.UnionWith(source.GlobalUsingsOf(project));
        foreach (var directive in unit.DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
        {
            if (SourceTypeIndex.NamespaceOf(directive) is { } ns)
                visible.Add(ns);
        }
        foreach (var declaration in unit.DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).OfType<BaseNamespaceDeclarationSyntax>())
        {
            // HobbyTracker.Application.Hobbies → HobbyTracker, HobbyTracker.Application, HobbyTracker.Application.Hobbies
            var parts = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Append(declaration)
                .SelectMany(ns => ns.Name.ToString().Split('.', StringSplitOptions.TrimEntries)).ToList();
            for (var count = 1; count <= parts.Count; count++)
                visible.Add(string.Join('.', parts.Take(count)));
        }
        return visible;
    }

    /// <summary>Using satırlarını mevcut using'lerin sonuna (hiç yoksa ilk tanımın önüne) ekler.</summary>
    internal static string InsertUsings(string content, CompilationUnitSyntax unit, IEnumerable<string> namespaces)
    {
        var newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = string.Concat(namespaces.Select(ns => $"using {ns};{newLine}"));
        if (unit.Usings.LastOrDefault() is { } lastUsing)
        {
            // Son using satırının sonu (satır sonu dahil); satır sonu yoksa (tek satırlık dosya) önce satır açıyoruz.
            var end = lastUsing.FullSpan.End;
            var endsLine = lastUsing.GetTrailingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            return content.Insert(end, endsLine ? lines : newLine + lines);
        }

        SyntaxNode? first = unit.Externs.LastOrDefault();
        if (first is not null)
            return content.Insert(first.FullSpan.End, lines);
        first = (SyntaxNode?)unit.AttributeLists.FirstOrDefault() ?? unit.Members.FirstOrDefault();
        return first is null ? content + newLine + lines : content.Insert(first.SpanStart, lines + newLine);
    }

    // Docker sandbox'ın ev klasörü (/home/sandbox) bu makinede DataDirectory/sandbox-home'dur (NuGet önbelleği orada).
    private string ToLocalPath(string path)
    {
        const string sandboxHome = DockerSandboxRunner.SandboxHome;
        if (path != sandboxHome && !path.StartsWith(sandboxHome + "/", StringComparison.Ordinal))
            return path;
        var local = DotnetBuildSandbox.HomeDirectory(storage.CurrentValue);
        return Path.Combine(local, path[sandboxHome.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Eksik using'ler eklendi: {Count} using, {FileCount} dosya")]
    private static partial void LogAdded(ILogger logger, int count, int fileCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Eksik using'ler eklenemedi: {ProjectRoot}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string projectRoot);

    /// <summary>Bir .csproj için: başvurduğu kütüphanelerin dizini ve SDK'nın ürettiği örtük global using'ler.</summary>
    private sealed record ProjectInfo(ReferenceTypeIndex References, IReadOnlySet<string> GeneratedGlobalUsings)
    {
        public static ProjectInfo None { get; } = new(ReferenceTypeIndex.Empty, new HashSet<string>());

        public static ProjectInfo Load(string projectFile, string projectRoot, Func<string, string> toLocalPath)
        {
            var references = ReferenceTypeIndex.ForProject(projectFile, projectRoot, toLocalPath);
            var generated = new HashSet<string>(StringComparer.Ordinal);
            // ImplicitUsings: SDK, derleme sırasında obj altına <Proje>.GlobalUsings.g.cs yazar (System, System.Linq ...).
            if (references.AssetsFile is { } assets)
            {
                foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(assets)!, "*.GlobalUsings.g.cs", SearchOption.AllDirectories))
                {
                    var unit = CSharpSyntaxTree.ParseText(File.ReadAllText(file), RoslynSyntaxChecker.ParseOptions).GetCompilationUnitRoot();
                    generated.UnionWith(unit.Usings.Select(SourceTypeIndex.NamespaceOf).OfType<string>());
                }
            }
            return new ProjectInfo(references, generated);
        }
    }
}
