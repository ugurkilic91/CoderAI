namespace DevAgents.Api.Hosting;

internal static class ConfigurationExtensions
{
    private const string LocalSettingsFile = "local.json";
    private const string DefaultUrl = "http://localhost:8080";

    /// <summary>
    /// Configuration order: appsettings.json → config/local.json (your overrides: models, paths, ...)
    /// → environment variables (Ollama__BaseUrl=...). Listens on http://localhost:8080 when no address is set.
    /// </summary>
    public static WebApplicationBuilder AddDevAgentsConfiguration(this WebApplicationBuilder builder)
    {
        var contentRoot = builder.Environment.ContentRootPath;
        var primary = Path.Combine(contentRoot, "config", LocalSettingsFile);                              // Docker: /app/config/local.json
        string?[] candidates =
        [
            primary,
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "config", LocalSettingsFile)),          // dotnet run from the repository
            Environment.GetEnvironmentVariable("DEVAGENTS_CONFIG")
        ];

        foreach (var path in candidates.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            // The primary file is registered even when missing, so a file created later is picked up on reload.
            if (path == primary || File.Exists(path))
                builder.Configuration.AddJsonFile(path!, optional: true, reloadOnChange: true);
        }

        // Environment variables win over config/local.json.
        builder.Configuration.AddEnvironmentVariables();

        // Docker sets ASPNETCORE_URLS=http://+:8080; otherwise only this machine can connect.
        if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            builder.WebHost.UseUrls(DefaultUrl);

        return builder;
    }
}
