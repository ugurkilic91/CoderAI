using System.Globalization;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Domain.ValueObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DevAgents.Infrastructure.CodeAnalysis;

/// <summary>
/// C# dosyalarını Roslyn ile ayrıştırıp sözdizimi hatalarını bulur. Derleme değildir (tipleri çözmez), ama eksik parantez,
/// yarım kalmış kod gibi hataları milisaniyeler içinde yakalar; bu dosyalar derlemeye gönderilmeden düzelttirilir.
/// </summary>
internal sealed class RoslynSyntaxChecker : ICodeSyntaxChecker
{
    internal static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public IReadOnlyList<CompilerDiagnostic> Check(IEnumerable<GeneratedFile> files) =>
    [
        .. files
            .Where(file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => CSharpSyntaxTree.ParseText(file.Content, ParseOptions, file.Path)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic =>
                {
                    var position = diagnostic.Location.GetLineSpan().StartLinePosition;
                    // Mesajlar İngilizce: düzeltme ajanı İngilizce çalışıyor.
                    return new CompilerDiagnostic(file.Path, position.Line + 1, position.Character + 1, diagnostic.Id,
                        diagnostic.GetMessage(CultureInfo.InvariantCulture));
                }))
    ];
}
