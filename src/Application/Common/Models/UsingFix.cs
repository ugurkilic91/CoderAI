namespace DevAgents.Application.Common.Models;

/// <summary>Bir dosyaya eklenen using satırları ve dosyanın yeni içeriği.</summary>
/// <param name="Path">Proje köküne göre göreli yol.</param>
public sealed record UsingFix(string Path, string Content, IReadOnlyList<string> AddedNamespaces);
