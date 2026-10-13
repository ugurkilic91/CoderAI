namespace DevAgents.Application.Context;

/// <summary>A file of the project being worked on. The content is read on demand, every time it is needed.</summary>
public sealed class ProjectFile(string relativePath, long size, Func<string> readContent)
{
    /// <summary>Path relative to the project root, with forward slashes.</summary>
    public string RelativePath { get; } = relativePath;

    public long Size { get; } = size;

    public string ReadContent() => readContent();

    public static ProjectFile FromContent(string relativePath, string content) =>
        new(relativePath, content.Length, () => content);
}
