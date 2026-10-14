using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.Enums;
using DevAgents.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevAgents.Application.FunctionalTests;

/// <summary>
/// Uygulamayı bellekte çalıştırır. Gerçek dosya sistemi kullanılır (geçici bir çalışma alanı);
/// model sunucusu, prompt'lar ve model seçimi ise deterministik sahtelerle değiştirilir.
/// </summary>
public sealed class DevAgentsApplicationFactory : WebApplicationFactory<Program>
{
    public DevAgentsApplicationFactory()
    {
        WorkspaceRoot = Directory.CreateTempSubdirectory("devagents-functional-").FullName;
        DataDirectory = Directory.CreateTempSubdirectory("devagents-functional-data-").FullName;
        WriteProjectFile("Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        WriteProjectFile("Shop/Program.cs", "var builder = WebApplication.CreateBuilder(args);");
        WriteProjectFile("Shop/Services/OrderService.cs", "public class OrderService { public void Cancel() { } }");
    }

    /// <summary>İzinli tek çalışma alanı kökü; içinde "Shop" adlı küçük bir proje var.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>DevAgents verileri (değişiklik geçmişi) için geçici klasör.</summary>
    public string DataDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Workspace:AllowedRoots:0", WorkspaceRoot);
        builder.UseSetting("Storage:DataDirectory", DataDirectory);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILanguageModelClient>();
            services.AddSingleton<ILanguageModelClient, ScriptedLanguageModel>();
            services.RemoveAll<IPromptProvider>();
            services.AddSingleton<IPromptProvider, PromptNameProvider>();
            services.RemoveAll<IAgentModelResolver>();
            services.AddSingleton<IAgentModelResolver, FixedModelResolver>();
            // Docker yerine: derleme ve testler hep başarılı. Dosya yazma ve git ile değişiklik takibi gerçek.
            services.RemoveAll<IBuildSandbox>();
            services.AddSingleton<IBuildSandbox, SucceedingSandbox>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(WorkspaceRoot))
            Directory.Delete(WorkspaceRoot, recursive: true);
        if (disposing && Directory.Exists(DataDirectory))
            Directory.Delete(DataDirectory, recursive: true);
    }

    private void WriteProjectFile(string relativePath, string content)
    {
        var path = Path.Combine(WorkspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

/// <summary>Sistem prompt'u olarak prompt'un adını verir; sahte model ajanları böyle ayırt eder.</summary>
internal sealed class PromptNameProvider : IPromptProvider
{
    public string GetPrompt(string name) => name;
}

internal sealed class FixedModelResolver : IAgentModelResolver
{
    public AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null) =>
        new(role.ToString(), $"model-{role.ToString().ToLowerInvariant()}", 0.1, 1000, 100);
}

/// <summary>
/// Ajana (prompt adına) ve isteğe göre sabit cevaplar veren sahte model. İstekte "ANSWERONLY" geçerse Router düz cevap seçer,
/// "FAIL" geçerse model sunucusu hatası taklit edilir; aksi hâlde tam geliştirme akışı çalışır.
/// </summary>
internal sealed class ScriptedLanguageModel : ILanguageModelClient
{
    public string Endpoint => "http://ollama.test";

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["model-a", "model-b"]);

    public async Task<string> ChatAsync(
        AgentModel agent, string systemPrompt, string userMessage, Func<string, Task>? onToken, CancellationToken cancellationToken)
    {
        if (userMessage.Contains("FAIL", StringComparison.Ordinal))
            throw new LanguageModelException($"Ollama'ya bağlanılamadı ({Endpoint}): bağlantı reddedildi. Ollama çalışıyor mu?");

        var answer = systemPrompt switch
        {
            "translator-to-en" => "EN: " + userMessage,
            "router" when userMessage.Contains("ANSWERONLY", StringComparison.Ordinal) =>
                """{"intent":"question","answer":true,"needsContext":false,"reason":"Genel soru."}""",
            "router" => """{"intent":"feature","analyze":true,"code":true,"test":true,"review":true,"needsContext":true,"reason":"Yeni özellik."}""",
            "answerer" => "A record is an immutable reference type with value equality.",
            "analyzer" => "### Goal\nAdd order cancellation.",
            "coder" => "### FILE: src/Foo.cs\n```csharp\nclass Foo {}\n```\n\n### NOTES\n- none",
            "tester" => "### FILE: tests/FooTests.cs\n```csharp\nclass FooTests {}\n```",
            "reviewer" => "VERDICT: APPROVED\n\n### Issues\nNone",
            "translator-to-tr" => "TR: " + userMessage,
            _ => ""
        };

        // Gerçek model gibi parça parça akıtıyoruz.
        if (onToken is not null && answer.Length > 0)
        {
            await onToken(answer[..(answer.Length / 2)]);
            await onToken(answer[(answer.Length / 2)..]);
        }
        return answer;
    }
}

/// <summary>Docker'sız sandbox: her derleme ve test çalıştırması başarılı sayılır.</summary>
internal sealed class SucceedingSandbox : IBuildSandbox
{
    public Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SandboxStatus("Docker", true, "test sandbox"));

    public Task<string> CreateProjectAsync(ArchitectureProfile architecture, string parentDirectory, string projectName,
        Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        var root = Path.Combine(parentDirectory, projectName);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, projectName + ".slnx"), "<Solution />");
        return Task.FromResult(root);
    }

    public async Task<BuildResult> BuildAsync(string projectRoot, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        if (onOutput is not null)
            await onOutput("Build succeeded.");
        return new BuildResult(true, [], "Build succeeded.", 0.1);
    }

    public Task<TestRunResult> RunTestsAsync(string projectRoot, IReadOnlyList<string> testProjects, Func<string, Task>? onOutput, CancellationToken cancellationToken) =>
        Task.FromResult(new TestRunResult(4, 4, 0, 0, [], "Passed!", 0.1));
}
