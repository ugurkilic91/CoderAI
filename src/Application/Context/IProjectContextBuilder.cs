using DevAgents.Application.Common.Exceptions;

namespace DevAgents.Application.Context;

/// <summary>Bir istek için proje bağlamını oluşturur.</summary>
public interface IProjectContextBuilder
{
    /// <summary>
    /// Proje klasörünü ve/veya istemcinin gönderdiği dosyaları indeksler, ardından bütçeye sığanları
    /// <paramref name="query"/> ile ilgisine göre seçer.
    /// </summary>
    /// <exception cref="ProjectPathException">Proje yolu izinli köklerin dışında ya da klasör yok.</exception>
    ProjectContext Build(string? projectPath, IEnumerable<InlineFile>? inlineFiles, string query, ContextBuildOptions? options = null);
}

/// <summary>Bağlamı bu istek için değiştiren ayarlar.</summary>
/// <param name="MaxContextChars">Dosyalar için karakter bütçesi; boşsa Workspace:MaxContextChars.</param>
/// <param name="ExcludedPaths">Bağlama eklenmeyecek dosyalar (örn. mimari örnek olarak zaten verilenler).</param>
public sealed record ContextBuildOptions(int? MaxContextChars = null, IReadOnlyCollection<string>? ExcludedPaths = null);
