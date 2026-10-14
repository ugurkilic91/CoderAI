using System.Diagnostics;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.Sandbox;

/// <summary>
/// Projeleri sandbox'ta oluşturur, derler ve test eder. Docker modunda:
/// paket geri yükleme ağ açık, derleme ve testler ağ kapalı çalışır; proje klasörü dışında hiçbir şey bağlanmaz.
/// </summary>
internal sealed class DotnetBuildSandbox(
    DockerSandboxRunner docker,
    ProcessSandboxRunner process,
    IOptionsMonitor<SandboxOptions> options,
    IOptionsMonitor<StorageOptions> storage) : IBuildSandbox
{
    private const string ProjectMount = "/workspace";
    private const string ResultsMount = "/results";
    private const int MaxOutputChars = 6000;

    // Ayar değişirse (Docker ↔ Process) yeniden başlatmadan uygulanır.
    private ISandboxRunner Runner =>
        string.Equals(options.CurrentValue.Mode, SandboxOptions.ProcessMode, StringComparison.OrdinalIgnoreCase) ? process : docker;

    public Task<SandboxStatus> GetStatusAsync(CancellationToken cancellationToken) => Runner.GetStatusAsync(cancellationToken);

    public async Task<string> CreateProjectAsync(
        ArchitectureProfile architecture, string parentDirectory, string projectName, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        if (!architecture.CanCreateProjects())
            throw new SandboxException($"'{architecture.Name}' mimarisi için şablon tanımlı değil; yeni proje oluşturulamaz.");

        var templateMount = $"/templates/{architecture.Id}";
        SandboxMount[] mounts =
        [
            new(parentDirectory, ProjectMount, ReadOnly: false),
            new(architecture.TemplateDirectory!, templateMount, ReadOnly: true),
            Home()
        ];
        var timeout = TimeSpan.FromMinutes(options.CurrentValue.BuildTimeoutMinutes);

        // 1) Şablonu sandbox'a kur (kalıcı ev klasörüne yazılır, sonraki seferler hızlıdır). Şablon yerelden gelir: ağ gerekmez.
        await RunCheckedAsync(new SandboxCommand(mounts, ProjectMount, ["new", "install", templateMount, "--force"], Network: false, timeout),
            onOutput, "Şablon kurulamadı", cancellationToken);

        // 2) Şablondan yeni projeyi oluştur (örn. dotnet new ca-sln ... --output Ad).
        var arguments = architecture.NewProjectArguments.Select(argument => argument.Replace("{name}", projectName)).ToList();
        await RunCheckedAsync(new SandboxCommand(mounts, ProjectMount, arguments, Network: false, timeout),
            onOutput, "Proje oluşturulamadı", cancellationToken);

        var projectRoot = Path.Combine(parentDirectory, projectName);
        return Directory.Exists(projectRoot)
            ? projectRoot
            : throw new SandboxException($"Şablon çalıştı ama proje klasörü oluşmadı: {projectRoot}");
    }

    public async Task<BuildResult> BuildAsync(string projectRoot, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var settings = options.CurrentValue;
        SandboxMount[] mounts = [new(projectRoot, ProjectMount, ReadOnly: false), Home()];

        // 1) Paket geri yükleme: NuGet'e ulaşmak için ağ açık (paketler kalıcı önbellekte birikir).
        var restore = await Runner.RunAsync(
            new SandboxCommand(mounts, ProjectMount, ["restore", BuildSettingsArgument()], Network: true, TimeSpan.FromMinutes(settings.RestoreTimeoutMinutes)),
            onOutput, cancellationToken);
        if (restore.ExitCode != 0)
            return Failed(restore, projectRoot, stopwatch);

        // 2) Derleme: ağ kapalı; üretilen kodun dışarıyla konuşmasına izin yok.
        var build = await Runner.RunAsync(
            new SandboxCommand(mounts, ProjectMount, ["build", "--no-restore", "-nologo", BuildSettingsArgument()], Network: false, TimeSpan.FromMinutes(settings.BuildTimeoutMinutes)),
            onOutput, cancellationToken);
        return build.ExitCode == 0
            ? new BuildResult(true, [], Tail(build.Output), stopwatch.Elapsed.TotalSeconds)
            : Failed(build, projectRoot, stopwatch);
    }

    public async Task<TestRunResult> RunTestsAsync(
        string projectRoot, IReadOnlyList<string> testProjects, Func<string, Task>? onOutput, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        // Sonuç dosyaları (TRX) projeyi kirletmesin diye ayrı bir geçici klasöre yazılır.
        var resultsDirectory = CreateSharedDirectory(Path.Combine(storage.CurrentValue.ResolveDataDirectory(), "test-results", Guid.NewGuid().ToString("N")));
        try
        {
            SandboxMount[] mounts = [new(projectRoot, ProjectMount, ReadOnly: false), new(resultsDirectory, ResultsMount, ReadOnly: false), Home()];
            var timeout = TimeSpan.FromMinutes(options.CurrentValue.TestTimeoutMinutes);
            IReadOnlyList<string> targets = testProjects.Count > 0 ? testProjects : [""];
            var output = new System.Text.StringBuilder();

            for (var index = 0; index < targets.Count; index++)
            {
                List<string> arguments = ["test", BuildSettingsArgument()];
                if (targets[index].Length > 0)
                    arguments.Add(targets[index]);
                arguments.AddRange(["--no-build", "--logger", $"trx;LogFileName=result{index}.trx", "--results-directory", ResultsMount]);
                // Testler ağ kapalı çalışır: yapay zekânın yazdığı test kodu dışarıya bağlanamaz.
                var run = await Runner.RunAsync(new SandboxCommand(mounts, ProjectMount, arguments, Network: false, timeout), onOutput, cancellationToken);
                output.AppendLine(run.Output);
            }

            var result = TestResultParser.FromTrxFiles(Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories))
                         ?? TestResultParser.FromConsole(output.ToString());
            return result with { Output = Tail(output.ToString()), Seconds = stopwatch.Elapsed.TotalSeconds };
        }
        finally
        {
            TryDelete(resultsDirectory);
        }
    }

    private async Task RunCheckedAsync(SandboxCommand command, Func<string, Task>? onOutput, string failure, CancellationToken cancellationToken)
    {
        var result = await Runner.RunAsync(command, onOutput, cancellationToken);
        if (result.ExitCode != 0)
            throw new SandboxException($"{failure}: {Tail(result.Output, 1500)}");
    }

    private static BuildResult Failed(CommandOutput output, string projectRoot, Stopwatch stopwatch)
    {
        var errors = MsBuildOutputParser.ParseErrors(output.Output, ProjectMount, projectRoot);
        // Hata satırı bulunamadıysa (zaman aşımı, araç hatası) çıktının sonunu hata olarak veriyoruz.
        if (errors.Count == 0)
            errors = [new CompilerDiagnostic("", 0, 0, output.TimedOut ? "TIMEOUT" : "BUILD", Tail(output.Output, 1500))];
        return new BuildResult(false, errors, Tail(output.Output), stopwatch.Elapsed.TotalSeconds);
    }

    /// <summary>Sandbox'taki dotnet'in ev klasörünün bu makinedeki yeri: NuGet önbelleği ve kurulu şablonlar burada kalıcıdır.</summary>
    internal static string HomeDirectory(StorageOptions storage) => Path.Combine(storage.ResolveDataDirectory(), "sandbox-home");

    private const string BuildSettingsFile = "devagents.targets";

    /// <summary>
    /// Sandbox derlemesine özel MSBuild ayarları; projenin dosyalarına dokunmadan her projenin sonunda içe aktarılır:
    /// - NuGet güvenlik açığı uyarıları (NU190x) TreatWarningsAsErrors ile hataya dönmesin: bunlar ajanın kodundan değil
    ///   paket sürümlerinden gelir, düzeltme ajanı çözemez (uyarı olarak çıktıda kalır).
    /// - Aspire AppHost projeleri derlenirken Aspire CLI paketini indirmeye çalışmasın: ağ kapalı sandbox'ta derleme kırılıyor.
    /// </summary>
    private const string BuildSettings = """
        <Project>
          <PropertyGroup>
            <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904;ASPIRE010</WarningsNotAsErrors>
            <AspireUseCliBundle>false</AspireUseCliBundle>
          </PropertyGroup>
        </Project>
        """;

    // Ayar dosyasını ev klasörüne yazar ve dotnet'e verilecek argümanı döndürür (Docker'da konteyner yolu, Process'te yerel yol).
    private string BuildSettingsArgument()
    {
        var path = Path.Combine(CreateSharedDirectory(HomeDirectory(storage.CurrentValue)), BuildSettingsFile);
        if (!File.Exists(path) || File.ReadAllText(path) != BuildSettings)
            File.WriteAllText(path, BuildSettings);
        var target = string.Equals(options.CurrentValue.Mode, SandboxOptions.ProcessMode, StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{DockerSandboxRunner.SandboxHome}/{BuildSettingsFile}";
        return $"-p:CustomAfterMicrosoftCommonTargets={target}";
    }

    private SandboxMount Home() =>
        new(CreateSharedDirectory(HomeDirectory(storage.CurrentValue)), DockerSandboxRunner.SandboxHome, ReadOnly: false);

    /// <summary>
    /// Sandbox kullanıcısı (proje sahibi) DevAgents'ten farklı olabileceği için klasörü herkese yazılabilir açıyoruz.
    /// </summary>
    private static string CreateSharedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, (UnixFileMode)0b111_111_111);
            }
            catch (UnauthorizedAccessException)
            {
                // Klasör başka bir kullanıcıya ait; mevcut izinleriyle devam ediyoruz.
            }
        }
        return path;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Geçici sonuç klasörü; silinemezse bir sonraki temizlikte gider.
        }
    }

    private static string Tail(string text, int maxChars = MaxOutputChars) =>
        text.Length <= maxChars ? text : "..." + text[^maxChars..];
}
