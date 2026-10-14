using System.Text;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Text;

namespace DevAgents.Application.Agents;

/// <summary>
/// Seçilen mimarinin ajanlara verilen kısmı: kurallar (rules.md) ve örnek dosyalar.
/// Model eğitmek yerine bu yolu kullanıyoruz: kurallar + gerçek örnekler + derleme/test geri bildirimi.
/// </summary>
internal sealed class ArchitectureGuide
{
    private const int MaxExampleChars = 4000;

    public static ArchitectureGuide None { get; } = new("", "", []);

    private ArchitectureGuide(string rulesText, string fullText, IReadOnlyCollection<string> examplePaths)
    {
        RulesText = rulesText;
        FullText = fullText;
        ExamplePaths = examplePaths;
    }

    /// <summary>Yalnızca kurallar (düzeltme ajanı için; bağlamı küçük tutuyoruz).</summary>
    public string RulesText { get; }

    /// <summary>Kurallar ve örnek dosyalar (analiz, kodlama, test ve review ajanları için).</summary>
    public string FullText { get; }

    /// <summary>Örnek olarak verilen proje dosyaları; proje bağlamına ikinci kez eklenmesinler diye.</summary>
    public IReadOnlyCollection<string> ExamplePaths { get; }

    public static ArchitectureGuide Build(ArchitectureProfile profile, string? projectRoot, IWorkspaceFileSystem workspace)
    {
        var rules = $"## ARCHITECTURE: {profile.Name}\nFollow these rules exactly; they override general habits.\n\n{profile.Rules.Trim()}";

        // Örnekler önce projenin kendisinden okunur (en güncel ve doğru ad alanlarıyla); yoksa şablondan.
        var examples = new StringBuilder();
        var examplePaths = new List<string>();
        foreach (var path in profile.ExampleFiles)
        {
            var content = (projectRoot is null ? null : workspace.ReadFile(projectRoot, path))
                ?? (profile.TemplateDirectory is null ? null : workspace.ReadFile(profile.TemplateDirectory, path));
            if (string.IsNullOrWhiteSpace(content))
                continue;
            examples.Append(FileBlock(path, content));
            examplePaths.Add(path);
        }
        // Ek örnekler şablonun adıyla yazılmıştır (örn. CleanArchitecture.Application); proje adıyla değiştiriyoruz ki
        // ajan ad alanlarını doğrudan doğru yazsın. Şablondan oluşturulan projelerde klasör adı = çözüm adı = kök ad alanı.
        var projectName = projectRoot is null ? null : Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var example in profile.ExtraExamples)
        {
            var content = profile.SourceName is { } sourceName && IsIdentifier(projectName)
                ? example.Content.Replace(sourceName, projectName, StringComparison.Ordinal)
                : example.Content;
            examples.Append(FileBlock(profile.SourceName is { } source && IsIdentifier(projectName) ? example.Path.Replace(source, projectName, StringComparison.Ordinal) : example.Path, content));
        }

        var full = examples.Length == 0
            ? rules
            : $"{rules}\n\n### Reference files (copy their structure and style; adapt names and namespaces to the task)\n{examples.ToString().TrimEnd()}";
        return new ArchitectureGuide(rules, full, examplePaths);
    }

    private static bool IsIdentifier([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? name) =>
        !string.IsNullOrEmpty(name) && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');

    private static string FileBlock(string path, string content) =>
        $"<file path=\"{path}\">\n{content.Trim('﻿').Truncate(MaxExampleChars).TrimEnd()}\n</file>\n";
}
