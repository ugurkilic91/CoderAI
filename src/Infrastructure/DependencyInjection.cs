using DevAgents.Application.Common.Interfaces;
using DevAgents.Infrastructure.Architectures;
using DevAgents.Infrastructure.CodeAnalysis;
using DevAgents.Infrastructure.Ollama;
using DevAgents.Infrastructure.Prompts;
using DevAgents.Infrastructure.Sandbox;
using DevAgents.Infrastructure.SourceControl;
using DevAgents.Infrastructure.Storage;
using DevAgents.Infrastructure.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    /// <summary>
    /// Infrastructure katmanını kaydeder: Ollama istemcisi, prompt ve mimari dosyaları, dosya sistemi,
    /// değişiklik takibi (git), Roslyn sözdizimi kontrolü ve derleme/test sandbox'ı.
    /// </summary>
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
        builder.Services.Configure<SandboxOptions>(builder.Configuration.GetSection(SandboxOptions.SectionName));
        builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

        // HttpClient zaman aşımı kapalı: cevaplar uzun sürebilir, her çağrı Ollama:TimeoutMinutes ile sınırlanıyor.
        builder.Services.AddHttpClient<ILanguageModelClient, OllamaChatClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddSingleton<IAgentModelResolver, OllamaAgentModelResolver>();
        builder.Services.AddSingleton<IPromptProvider, FilePromptProvider>();
        builder.Services.AddSingleton<IArchitectureCatalog, FileArchitectureCatalog>();

        builder.Services.AddSingleton<IWorkspaceFileSystem, FileSystemWorkspace>();
        builder.Services.AddSingleton<IProjectWriter, FileSystemProjectWriter>();
        builder.Services.AddSingleton<IChangeTracker, GitChangeTracker>();
        builder.Services.AddSingleton<ICodeSyntaxChecker, RoslynSyntaxChecker>();

        // Sandbox: Docker (varsayılan) ya da Process; hangisinin kullanılacağı her çağrıda "Sandbox:Mode" ayarından okunur.
        builder.Services.AddSingleton<DockerSandboxRunner>();
        builder.Services.AddSingleton<ProcessSandboxRunner>();
        builder.Services.AddSingleton<IBuildSandbox, DotnetBuildSandbox>();
    }
}
