using System.Reflection;
using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Development;
using DevAgents.Application.Common.Behaviours;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    /// <summary>
    /// Application katmanını kaydeder: MediatR (komut/sorgu işleyicileri ve pipeline davranışları),
    /// FluentValidation doğrulayıcıları, ajan akışı ve bağlam motorları, "Pipeline", "Workspace" ve "Development" ayarları.
    /// </summary>
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.SectionName));
        builder.Services.Configure<WorkspaceOptions>(builder.Configuration.GetSection(WorkspaceOptions.SectionName));
        builder.Services.Configure<DevelopmentOptions>(builder.Configuration.GetSection(DevelopmentOptions.SectionName));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            // Normal komut/sorgular: loglama → hata loglama → doğrulama → performans ölçümü → işleyici.
            cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
            // Akış (stream) istekleri (ajan akışı): hata loglama → doğrulama → işleyici.
            cfg.AddOpenStreamBehavior(typeof(StreamUnhandledExceptionBehaviour<,>));
            cfg.AddOpenStreamBehavior(typeof(StreamValidationBehaviour<,>));
        });

        builder.Services.AddSingleton<IProjectContextBuilder, ProjectContextBuilder>();
        builder.Services.AddScoped<IAgentPipeline, AgentPipeline>();
        // Kodu projeye uygulayan akış ve proje başına yazma kilidi (kilit tüm istekler arasında paylaşılır).
        builder.Services.AddScoped<DevelopmentWorkflow>();
        builder.Services.AddSingleton<ProjectLocks>();
    }
}
