using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>
/// Projeyi yalıtılmış bir ortamda (varsayılan: Docker konteyneri, ağ kapalı) oluşturur, derler ve test eder.
/// Yapay zekânın yazdığı kod ve testler sunucuda doğrudan değil, bu ortamda çalışır.
/// </summary>
public interface IBuildSandbox
{
    /// <summary>Sandbox kullanılabilir mi (örn. Docker çalışıyor mu); arayüzde gösterilir.</summary>
    Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Mimarinin şablonundan <paramref name="parentDirectory"/> altında yeni bir proje oluşturur ve kök klasörünü döndürür.</summary>
    /// <exception cref="SandboxException">Komut çalıştırılamadı ya da başarısız oldu.</exception>
    Task<string> CreateProjectAsync(ArchitectureProfile architecture, string parentDirectory, string projectName,
        Func<string, Task>? onOutput, CancellationToken cancellationToken);

    /// <summary>Paketleri geri yükler (ağ açık) ve projeyi derler (ağ kapalı).</summary>
    Task<BuildResult> BuildAsync(string projectRoot, Func<string, Task>? onOutput, CancellationToken cancellationToken);

    /// <summary>Verilen test projelerini derlenmiş hâliyle çalıştırır (ağ kapalı).</summary>
    Task<TestRunResult> RunTestsAsync(string projectRoot, IReadOnlyList<string> testProjects,
        Func<string, Task>? onOutput, CancellationToken cancellationToken);
}

/// <param name="Mode">"Docker" ya da "Process".</param>
public sealed record SandboxStatus(string Mode, bool Available, string Message);
