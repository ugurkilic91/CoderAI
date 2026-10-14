namespace DevAgents.Domain.ValueObjects;

/// <summary>Bir projenin derlenmesinin sonucu (dotnet build).</summary>
/// <param name="Errors">Derleyici hataları; kodlama ajanına düzeltme için bu liste gönderilir.</param>
/// <param name="Output">Komut çıktısının sonu (hata ayıklamak için).</param>
public sealed record BuildResult(bool Succeeded, IReadOnlyList<CompilerDiagnostic> Errors, string Output, double Seconds);

/// <summary>Derleyicinin ya da Roslyn sözdizimi kontrolünün bildirdiği bir hata.</summary>
/// <param name="File">Projeye göre göreli dosya yolu (bilinmiyorsa boş).</param>
public sealed record CompilerDiagnostic(string File, int Line, int Column, string Code, string Message)
{
    /// <summary>Ajanlara ve rapora yazılan biçim: <c>src/A.cs(12,5): CS0246 mesaj</c>.</summary>
    public override string ToString() =>
        File.Length == 0 ? $"{Code}: {Message}" : $"{File}({Line},{Column}): {Code} {Message}";
}
