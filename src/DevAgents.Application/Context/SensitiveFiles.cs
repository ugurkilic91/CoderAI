namespace DevAgents.Application.Context;

/// <summary>Files that may hold secrets. They only reach a model when the user attaches (pins) them explicitly.</summary>
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
