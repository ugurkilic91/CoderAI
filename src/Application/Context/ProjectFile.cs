namespace DevAgents.Application.Context;

/// <summary>Üzerinde çalışılan projenin bir dosyası. İçerik bellekte tutulmaz; her gerektiğinde yeniden okunur.</summary>
public sealed class ProjectFile(string relativePath, long size, Func<string> readContent)
{
    /// <summary>Proje köküne göre göreli yol, ileri eğik çizgili.</summary>
    public string RelativePath { get; } = relativePath;

    public long Size { get; } = size;

    public string ReadContent() => readContent();

    public static ProjectFile FromContent(string relativePath, string content) =>
        new(relativePath, content.Length, () => content);
}
