namespace DevAgents.Domain.Files;

/// <summary>A source file produced by the coder or tester agent.</summary>
/// <param name="Path">Project-relative path, normalized by <see cref="RelativePath.Normalize"/>.</param>
/// <param name="Kind">One of <see cref="GeneratedFileKind"/>.</param>
public sealed record GeneratedFile(string Path, string Content, string Kind);

public static class GeneratedFileKind
{
    public const string Code = "code";
    public const string Test = "test";
}
