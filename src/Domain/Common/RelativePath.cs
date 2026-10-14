namespace DevAgents.Domain.Common;

/// <summary>İstemciden ya da model çıktısından gelen, projeye göre göreli dosya yolları.</summary>
public static class RelativePath
{
    /// <summary>
    /// Yolu ileri eğik çizgili hâle getirir; çevresindeki tırnak/backtick işaretlerini ve baştaki "/" ya da "./" kısmını atar.
    /// Yol boşsa, ".." içeriyorsa (projenin dışına çıkabilir) ya da ":" içeriyorsa (sürücü harfi, URL) <c>null</c> döner.
    /// Bu kural, ajanların proje dışına dosya yazmasını engelleyen ilk güvenlik kontrolüdür.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null)
            return null;

        var path = raw.Trim().Trim('`', '"', '\'', '*').Replace('\\', '/').TrimStart('/');
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];

        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal) || path.Contains(':'))
            return null;
        return path;
    }
}
