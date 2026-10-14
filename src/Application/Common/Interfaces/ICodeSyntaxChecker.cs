using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Üretilen C# dosyalarının sözdizimini derleme yapmadan (Roslyn ile) saniyeler içinde kontrol eder.</summary>
public interface ICodeSyntaxChecker
{
    /// <summary>Yalnızca .cs dosyalarına bakar; hata yoksa boş liste döner.</summary>
    IReadOnlyList<CompilerDiagnostic> Check(IEnumerable<GeneratedFile> files);
}
