using DevAgents.Api.Hosting;

namespace DevAgents.Api;

public static class DependencyInjection
{
    /// <summary>Registers the web layer: exception → problem details mapping and the clock used in responses.</summary>
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
