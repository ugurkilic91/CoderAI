namespace DevAgents.Domain.ValueObjects;

/// <summary>Ajanların projede yaptığı bir dosya değişikliği.</summary>
/// <param name="Path">Projeye göre göreli yol.</param>
/// <param name="Kind"><see cref="FileChangeKind"/> değerlerinden biri.</param>
public sealed record FileChange(string Path, string Kind);

public static class FileChangeKind
{
    public const string Created = "created";
    public const string Modified = "modified";
    public const string Deleted = "deleted";
}
