using DevAgents.Application.Common.Interfaces;
using DevAgents.Infrastructure.Ollama;
using DevAgents.Infrastructure.Prompts;
using DevAgents.Infrastructure.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevAgents.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the Ollama client, the prompt files and the file-system workspace; binds the "Ollama" section.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        // No HttpClient timeout: generations can be long; each call is bounded by Ollama:TimeoutMinutes instead.
        services.AddHttpClient<ILanguageModelClient, OllamaChatClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IAgentModelResolver, OllamaAgentModelResolver>();
        services.AddSingleton<IPromptProvider, FilePromptProvider>();
        services.AddSingleton<IWorkspaceFileSystem, FileSystemWorkspace>();
        return services;
    }
}
