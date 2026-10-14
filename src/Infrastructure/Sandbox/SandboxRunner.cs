using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Sandbox;

/// <summary>Sandbox'a bağlanan bir klasör: ana makinedeki yol → sandbox içindeki yol.</summary>
internal sealed record SandboxMount(string HostPath, string ContainerPath, bool ReadOnly);

/// <summary>Sandbox'ta çalışacak bir dotnet komutu.</summary>
/// <param name="WorkingDirectory">Sandbox içindeki çalışma klasörü (bağlamalardan birinin yolu).</param>
/// <param name="Arguments">dotnet'e verilecek argümanlar (kabuk yok; her argüman ayrı geçirilir).</param>
/// <param name="Network">Ağ açık mı (yalnızca paket geri yükleme için).</param>
internal sealed record SandboxCommand(
    IReadOnlyList<SandboxMount> Mounts,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    bool Network,
    TimeSpan Timeout);

internal sealed record CommandOutput(int ExitCode, string Output, bool TimedOut);

/// <summary>Bir dotnet komutunu yalıtılmış ortamda çalıştırır.</summary>
internal interface ISandboxRunner
{
    Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<CommandOutput> RunAsync(SandboxCommand command, Func<string, Task>? onOutput, CancellationToken cancellationToken);
}

/// <summary>
/// Her komutu ayrı bir Docker konteynerinde çalıştırır: --rm (iş bitince silinir), bellek/CPU/işlem sınırı,
/// varsayılan olarak ağ kapalı, yalnızca proje klasörü bağlı. Üretilen kod sunucunun geri kalanına erişemez.
/// </summary>
internal sealed class DockerSandboxRunner(IOptionsMonitor<SandboxOptions> options) : ISandboxRunner
{
    public const string SandboxHome = "/home/sandbox";

    public async Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = await ProcessRunner.RunAsync("docker", ["version", "--format", "{{.Server.Version}}"], null, null,
                TimeSpan.FromSeconds(10), null, cancellationToken);
            return version.ExitCode == 0
                ? new SandboxStatus(SandboxOptions.DockerMode, true, $"Docker {version.Output.Trim()} · {options.CurrentValue.Image}")
                : new SandboxStatus(SandboxOptions.DockerMode, false, $"Docker çalışmıyor: {version.Output.Trim()}");
        }
        catch (SandboxException ex)
        {
            return new SandboxStatus(SandboxOptions.DockerMode, false, ex.Message);
        }
    }

    public async Task<CommandOutput> RunAsync(SandboxCommand command, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var name = "devagents-" + Guid.NewGuid().ToString("N")[..12];
        List<string> arguments =
        [
            "run", "--rm", "--name", name, "--label", "devagents=sandbox",
            "--network", command.Network ? "bridge" : "none",
            "--memory", settings.Memory, "--cpus", settings.Cpus, "--pids-limit", settings.PidsLimit.ToString(),
            // dotnet'in ev klasörü (NuGet önbelleği ve kurulu şablonlar burada kalır, sonraki çalıştırmalar hızlanır).
            "-e", $"HOME={SandboxHome}", "-e", $"DOTNET_CLI_HOME={SandboxHome}", "-e", $"NUGET_PACKAGES={SandboxHome}/.nuget/packages",
            "-e", "DOTNET_NOLOGO=1", "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "-e", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1",
            "-e", "DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1", "-e", "NUGET_XMLDOC_MODE=skip"
        ];

        var user = await ResolveUserAsync(settings.User, command.Mounts, cancellationToken);
        if (user is not null)
            arguments.AddRange(["--user", user]);
        foreach (var mount in command.Mounts)
            arguments.AddRange(["-v", $"{ToHostPath(mount.HostPath, settings)}:{mount.ContainerPath}{(mount.ReadOnly ? ":ro" : "")}"]);
        arguments.AddRange(["-w", command.WorkingDirectory, settings.Image, "dotnet", .. command.Arguments]);

        // İptal ya da zaman aşımında docker istemcisini öldürmek konteyneri durdurmaz; konteyneri adıyla ayrıca öldürüyoruz.
        return await ProcessRunner.RunAsync("docker", arguments, null, null, command.Timeout, onOutput, cancellationToken,
            onAbort: () => ProcessRunner.RunAsync("docker", ["kill", name], null, null, TimeSpan.FromSeconds(30), null, CancellationToken.None));
    }

    // DevAgents bir konteynerdeyse, bağlanacak klasörün ana makinedeki karşılığı.
    private static string ToHostPath(string path, SandboxOptions settings)
    {
        foreach (var mapping in settings.PathMappings.Where(m => m.Container.Length > 0 && m.Host.Length > 0))
        {
            var prefix = mapping.Container.TrimEnd('/');
            if (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal))
                return mapping.Host.TrimEnd('/') + path[prefix.Length..];
        }
        return path;
    }

    // "auto": yazılabilir ilk bağlamanın (proje klasörü) sahibi; böylece bin/obj ve yeni dosyalar root'a ait olmaz.
    private static async Task<string?> ResolveUserAsync(string user, IReadOnlyList<SandboxMount> mounts, CancellationToken cancellationToken)
    {
        if (!string.Equals(user, "auto", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(user) ? null : user;
        if (!OperatingSystem.IsLinux())
            return null;

        var projectMount = mounts.FirstOrDefault(m => !m.ReadOnly);
        if (projectMount is null)
            return null;
        var owner = await ProcessRunner.RunAsync("stat", ["-c", "%u:%g", projectMount.HostPath], null, null, TimeSpan.FromSeconds(10), null, cancellationToken);
        return owner.ExitCode == 0 ? owner.Output.Trim() : null;
    }
}

/// <summary>
/// Docker'sız mod: dotnet doğrudan bu makinede çalışır. Sandbox yolları, bağlamalardaki karşılıklarına çevrilir.
/// Üretilen kod DevAgents'in yetkileriyle çalıştığı için yalnızca tek kişilik yerel kullanım içindir.
/// </summary>
internal sealed class ProcessSandboxRunner : ISandboxRunner
{
    public async Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = await ProcessRunner.RunAsync("dotnet", ["--version"], null, null, TimeSpan.FromSeconds(10), null, cancellationToken);
            return new SandboxStatus(SandboxOptions.ProcessMode, version.ExitCode == 0,
                $".NET SDK {version.Output.Trim()} (Docker'sız; üretilen kod bu makinede doğrudan çalışır)");
        }
        catch (SandboxException ex)
        {
            return new SandboxStatus(SandboxOptions.ProcessMode, false, ex.Message);
        }
    }

    public Task<CommandOutput> RunAsync(SandboxCommand command, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        string Local(string path)
        {
            foreach (var mount in command.Mounts.OrderByDescending(m => m.ContainerPath.Length))
            {
                if (path == mount.ContainerPath || path.StartsWith(mount.ContainerPath + "/", StringComparison.Ordinal))
                    return mount.HostPath + path[mount.ContainerPath.Length..].Replace('/', Path.DirectorySeparatorChar);
            }
            return path;
        }

        var arguments = command.Arguments.Select(Local).ToList();
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        };
        return ProcessRunner.RunAsync("dotnet", arguments, Local(command.WorkingDirectory), environment, command.Timeout, onOutput, cancellationToken);
    }
}

/// <summary>Bir işlemi çalıştırır, çıktısını satır satır iletir, zaman aşımı ve iptali uygular.</summary>
internal static class ProcessRunner
{
    private const int MaxCapturedChars = 200_000;

    public static async Task<CommandOutput> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        Func<string, Task>? onOutput,
        CancellationToken cancellationToken,
        Func<Task>? onAbort = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? ""
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[key] = value;

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new SandboxException($"{fileName} başlatılamadı.");
        }
        catch (Win32Exception ex)
        {
            throw new SandboxException($"{fileName} bulunamadı. Sandbox için {(fileName == "docker" ? "Docker" : ".NET SDK")} kurulu olmalı.", ex);
        }

        using (process)
        {
            var output = new StringBuilder();
            var gate = new SemaphoreSlim(1, 1);
            async Task Pump(StreamReader reader)
            {
                while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
                {
                    await gate.WaitAsync(CancellationToken.None);
                    try
                    {
                        if (output.Length < MaxCapturedChars)
                            output.AppendLine(line);
                        if (onOutput is not null)
                            await onOutput(line);
                    }
                    catch (OperationCanceledException)
                    {
                        // İstemci bağlantıyı kesti; çıktıyı toplamaya devam ediyoruz, işlem aşağıda durdurulacak.
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
            }

            var pumps = Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError));
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                await pumps;
                return new CommandOutput(process.ExitCode, output.ToString(), TimedOut: false);
            }
            catch (OperationCanceledException)
            {
                // Zaman aşımı ya da iptal: işlemi (ve docker ise konteyneri) durduruyoruz.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // İşlem zaten bitmiş.
                }
                if (onAbort is not null)
                    await onAbort();
                if (cancellationToken.IsCancellationRequested)
                    throw;

                // Kalan çıktının okunmasını kısa süre bekleyip çıktıyı kilit altında alıyoruz.
                await Task.WhenAny(pumps, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
                await gate.WaitAsync(CancellationToken.None);
                var captured = output.ToString();
                gate.Release();
                return new CommandOutput(-1, captured + $"\n[DevAgents] Komut {timeout.TotalMinutes:0} dakikada bitmedi ve durduruldu.", TimedOut: true);
            }
        }
    }
}
