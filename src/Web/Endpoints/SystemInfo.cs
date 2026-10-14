using DevAgents.Application.SystemInfo.Queries.GetConfiguration;
using DevAgents.Application.SystemInfo.Queries.GetHealth;
using DevAgents.Application.SystemInfo.Queries.GetModels;
using DevAgents.Application.SystemInfo.Queries.GetProjects;

namespace DevAgents.Web.Endpoints;

/// <summary>Web arayüzünün kenar çubuğu ve tanılama için yardımcı uç noktalar.</summary>
public class SystemInfo : IEndpointGroup
{
    public static string? RoutePrefix => "/api";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetHealth, "health");
        groupBuilder.MapGet(GetModels, "models");
        groupBuilder.MapGet(GetConfiguration, "config");
        groupBuilder.MapGet(GetProjects, "projects");
    }

    [EndpointSummary("Ollama'ya ulaşılabiliyor mu")]
    [EndpointDescription("Her zaman 200 döner; Ollama'ya ulaşılamıyorsa bu, cevabın içinde (ok: false) bildirilir.")]
    public static async Task<IResult> GetHealth(ISender sender, CancellationToken cancellationToken)
    {
        var health = await sender.Send(new GetHealthQuery(), cancellationToken);
        // "ollama" alan adı istemcilerle olan sözleşmenin parçası.
        return health.IsHealthy
            ? Results.Ok(new { ok = true, ollama = health.Endpoint, modelCount = health.ModelCount })
            : Results.Ok(new { ok = false, ollama = health.Endpoint, error = health.Error });
    }

    [EndpointSummary("Ollama'da yüklü modeller")]
    public static async Task<IResult> GetModels(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetModelsQuery(), cancellationToken));

    [EndpointSummary("Geçerli ayarlar")]
    [EndpointDescription("Her ajanın modeli, akış/çalışma alanı/geliştirme ayarları ve derleme sandbox'ının (Docker) durumu.")]
    public static async Task<IResult> GetConfiguration(ISender sender, CancellationToken cancellationToken)
    {
        var configuration = await sender.Send(new GetConfigurationQuery(), cancellationToken);
        return Results.Ok(new
        {
            ollama = configuration.Endpoint,
            agents = configuration.Agents,
            pipeline = configuration.Pipeline,
            workspace = configuration.Workspace,
            development = configuration.Development,
            sandbox = configuration.Sandbox
        });
    }

    [EndpointSummary("Çalışma alanındaki projeler")]
    public static async Task<IResult> GetProjects(ISender sender, CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(new GetProjectsQuery(), cancellationToken));
}
