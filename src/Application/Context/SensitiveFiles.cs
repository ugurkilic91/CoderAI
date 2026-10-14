namespace DevAgents.Application.Context;

/// <summary>Gizli bilgi taşıyabilecek dosyalar. Kullanıcı açıkça sabitlemedikçe hiçbir modele gönderilmez.</summary>
internal static class SensitiveFiles
{
    public static bool IsSensitive(string relativePath)
    {
        var name = Path.GetFileName(relativePath).ToLowerInvariant();
        return name.Contains("secret", StringComparison.Ordinal)
            || name.StartsWith(".env", StringComparison.Ordinal)
            || name.EndsWith(".pfx", StringComparison.Ordinal)
            || name.EndsWith(".pem", StringComparison.Ordinal)
            || name.EndsWith(".key", StringComparison.Ordinal)
            || name.Contains("appsettings.production", StringComparison.Ordinal);
    }
}
