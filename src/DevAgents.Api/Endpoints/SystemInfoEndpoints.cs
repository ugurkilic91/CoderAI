using DevAgents.Application.SystemInfo;

namespace DevAgents.Api.Endpoints;

/// <summary>Helper endpoints used by the web UI's sidebar and for diagnostics.</summary>
internal static class SystemInfoEndpoints
{
    public static IEndpointRouteBuilder MapSystemInfoEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", GetHealthAsync);
        api.MapGet("/models", GetModelsAsync);
        api.MapGet("/config", GetConfiguration);
        api.MapGet("/projects", GetProjects);
        return app;
    }

    // Always 200: an unreachable Ollama is reported in the body, it is not an error of this endpoint.
    private static async Task<IResult> GetHealthAsync(ISystemInfoService systemInfo, CancellationToken cancellationToken)
    {
        var health = await systemInfo.CheckHealthAsync(cancellationToken);
        return health.IsHealthy
            ? Results.Ok(new { ok = true, ollama = health.Endpoint, modelCount = health.ModelCount })
            : Results.Ok(new { ok = false, ollama = health.Endpoint, error = health.Error });
    }

    private static async Task<IResult> GetModelsAsync(ISystemInfoService systemInfo, CancellationToken cancellationToken) =>
        Results.Ok(await systemInfo.ListModelsAsync(cancellationToken));

    private static IResult GetConfiguration(ISystemInfoService systemInfo)
    {
        var configuration = systemInfo.GetConfiguration();
        return Results.Ok(new
        {
            ollama = configuration.Endpoint,
            agents = configuration.Agents,
            pipeline = configuration.Pipeline,
            workspace = configuration.Workspace
        });
    }

    private static IResult GetProjects(ISystemInfoService systemInfo) => Results.Ok(systemInfo.ListProjects());
}
