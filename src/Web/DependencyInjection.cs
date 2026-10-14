namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    private const string LocalSettingsFile = "local.json";
    private const string DefaultUrl = "http://localhost:8080";

    /// <summary>Web katmanını kaydeder: hata → ProblemDetails çevirisi, OpenAPI dokümanı, cevaplarda kullanılan saat.</summary>
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi();

        builder.Services.AddSingleton(TimeProvider.System);
    }

    /// <summary>
    /// Yapılandırma sırası: appsettings.json → config/local.json (kendi ayarlarınız: modeller, yollar...)
    /// → ortam değişkenleri (Ollama__BaseUrl=...). Adres verilmemişse yalnızca bu makineden erişilebilen
    /// http://localhost:8080 adresini dinler.
    /// </summary>
    public static void AddDevAgentsConfiguration(this WebApplicationBuilder builder)
    {
        var contentRoot = builder.Environment.ContentRootPath;
        var primary = Path.Combine(contentRoot, "config", LocalSettingsFile);                              // Docker: /app/config/local.json
        string?[] candidates =
        [
            primary,
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "config", LocalSettingsFile)),          // repodan dotnet run
            Environment.GetEnvironmentVariable("DEVAGENTS_CONFIG")
        ];

        foreach (var path in candidates.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            // Birincil dosya henüz yoksa da kaydediyoruz: sonradan oluşturulursa değişiklik izleyicisi onu alır.
            if (path == primary || File.Exists(path))
                builder.Configuration.AddJsonFile(path!, optional: true, reloadOnChange: true);
        }

        // Ortam değişkenleri config/local.json'ı ezer.
        builder.Configuration.AddEnvironmentVariables();

        // Docker ASPNETCORE_URLS=http://+:8080 verir; yerelde varsayılan olarak yalnızca bu makine bağlanabilir.
        if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            builder.WebHost.UseUrls(DefaultUrl);
    }
}
