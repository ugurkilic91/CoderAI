using DevAgents.Infrastructure.Sandbox;
using DevAgents.Infrastructure.Storage;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Infrastructure.IntegrationTests.Sandbox;

/// <summary>
/// Gerçek bir dotnet build: önce bu makinede (Process modu), Docker varsa konteynerde (Docker modu).
/// Küçük bir sınıf kütüphanesi kasıtlı bir derleme hatasıyla derlenir; hata göreli yoluyla okunmalı, düzeltince derleme geçmeli.
/// </summary>
[Category("Slow")]
public class DotnetBuildSandboxTests
{
    private string _project = null!;
    private string _data = null!;

    [SetUp]
    public void SetUp()
    {
        _project = Directory.CreateTempSubdirectory("devagents-build-").FullName;
        _data = Directory.CreateTempSubdirectory("devagents-sandbox-data-").FullName;
        File.WriteAllText(Path.Combine(_project, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
            </Project>
            """);
        Directory.CreateDirectory(Path.Combine(_project, "Features"));
        File.WriteAllText(Path.Combine(_project, "Features", "Hobby.cs"), "namespace Lib;\npublic class Hobby { public Missing Name { get; set; } }\n");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_project, recursive: true);
        Directory.Delete(_data, recursive: true);
    }

    private DotnetBuildSandbox CreateSandbox(string mode)
    {
        var options = new StaticOptionsMonitor<SandboxOptions>(new SandboxOptions { Mode = mode });
        return new DotnetBuildSandbox(new DockerSandboxRunner(options), new ProcessSandboxRunner(), options,
            new StaticOptionsMonitor<StorageOptions>(new StorageOptions { DataDirectory = _data }));
    }

    [TestCase(SandboxOptions.ProcessMode)]
    [TestCase(SandboxOptions.DockerMode)]
    public async Task ShouldReportCompilerErrorsAndBuildOnceFixed(string mode)
    {
        var sandbox = CreateSandbox(mode);
        var status = await sandbox.GetStatusAsync(CancellationToken.None);
        Assume.That(status.Available, $"{mode} sandbox kullanılamıyor: {status.Message}");
        var lines = new List<string>();

        var failed = await sandbox.BuildAsync(_project, line => { lines.Add(line); return Task.CompletedTask; }, CancellationToken.None);

        failed.Succeeded.ShouldBeFalse();
        var error = failed.Errors.ShouldHaveSingleItem();
        (error.File, error.Line, error.Code).ShouldBe(("Features/Hobby.cs", 2, "CS0246"));
        lines.ShouldNotBeEmpty();

        await File.WriteAllTextAsync(Path.Combine(_project, "Features", "Hobby.cs"), "namespace Lib;\npublic class Hobby { public string? Name { get; set; } }\n");
        var fixedBuild = await sandbox.BuildAsync(_project, null, CancellationToken.None);

        fixedBuild.Succeeded.ShouldBeTrue();
        fixedBuild.Errors.ShouldBeEmpty();
    }
}
