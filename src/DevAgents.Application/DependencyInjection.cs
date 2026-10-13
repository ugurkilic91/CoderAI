using DevAgents.Application.Chat;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Application.SystemInfo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevAgents.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use cases and binds the "Pipeline" and "Workspace" configuration sections.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));
        services.Configure<WorkspaceOptions>(configuration.GetSection(WorkspaceOptions.SectionName));

        services.AddSingleton<IProjectContextBuilder, ProjectContextBuilder>();
        services.AddScoped<IAgentPipeline, AgentPipeline>();
        services.AddScoped<ISystemInfoService, SystemInfoService>();
        return services;
    }
}
