namespace DevAgents.Domain.ValueObjects;

/// <summary>Kodlama ya da test ajanının ürettiği bir kaynak dosya.</summary>
/// <param name="Path">Projeye göre göreli yol; <see cref="Common.RelativePath.Normalize"/> ile temizlenmiştir.</param>
/// <param name="Kind"><see cref="GeneratedFileKind"/> değerlerinden biri.</param>
public sealed record GeneratedFile(string Path, string Content, string Kind);

public static class GeneratedFileKind
{
    public const string Code = "code";
    public const string Test = "test";
}
