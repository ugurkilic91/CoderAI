namespace DevAgents.Domain.Files;

/// <summary>Project-relative file paths coming from clients or from model output.</summary>
public static class RelativePath
{
    /// <summary>
    /// Returns the path with forward slashes and without surrounding quotes/backticks or a leading "/" or "./".
    /// Returns <c>null</c> when the path is empty, contains ".." (could leave the project)
    /// or contains ":" (drive letters, URIs).
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
