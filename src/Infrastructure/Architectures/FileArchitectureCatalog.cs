using System.Text.Json;
using System.Text.RegularExpressions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevAgents.Infrastructure.Architectures;

/// <summary>
/// Mimari profillerini uygulamanın yanındaki architectures/ klasöründen okur: her alt klasör bir profildir
/// (profile.json + rules.md + examples/). Dosyalar her çağrıda okunur; düzenlemeler yeniden başlatmadan geçerli olur.
/// </summary>
internal sealed partial class FileArchitectureCatalog(IHostEnvironment environment, ILogger<FileArchitectureCatalog> logger)
    : IArchitectureCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "architectures");

    public IReadOnlyList<ArchitectureProfile> GetAll() =>
        Directory.Exists(_directory)
            ? [.. Directory.GetDirectories(_directory).OrderBy(path => path, StringComparer.Ordinal).Select(Load).OfType<ArchitectureProfile>()]
            : [];

    public ArchitectureProfile? Find(string id) =>
        SafeId().IsMatch(id) && Directory.Exists(Path.Combine(_directory, id)) ? Load(Path.Combine(_directory, id)) : null;

    private ArchitectureProfile? Load(string directory)
    {
        var id = Path.GetFileName(directory);
        var profilePath = Path.Combine(directory, "profile.json");
        if (!SafeId().IsMatch(id) || !File.Exists(profilePath))
            return null;

        try
        {
            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(profilePath), JsonOptions) ?? new ProfileFile();
            var rulesPath = Path.Combine(directory, "rules.md");
            var rules = File.Exists(rulesPath) ? File.ReadAllText(rulesPath) : "";

            // Ek örnekler profil klasöründe durur; ajanlara projedeki hedef yollarıyla gösterilir.
            var extraExamples = (file.ExtraExamples ?? [])
                .Where(example => !string.IsNullOrWhiteSpace(example.File) && !string.IsNullOrWhiteSpace(example.Path))
                .Select(example => (example.Path!, Full: Path.GetFullPath(Path.Combine(directory, example.File!))))
                .Where(example => example.Full.StartsWith(directory, StringComparison.Ordinal) && File.Exists(example.Full))
                .Select(example => new ArchitectureExample(example.Item1, File.ReadAllText(example.Full)))
                .ToList();

            return new ArchitectureProfile(
                id,
                file.Name ?? id,
                file.Description ?? "",
                rules,
                ResolveTemplateDirectory(file.TemplateDirectory),
                string.IsNullOrWhiteSpace(file.SourceName) ? null : file.SourceName,
                file.NewProject ?? [],
                file.TestProjects ?? [],
                file.Examples ?? [],
                extraExamples);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogInvalidProfile(logger, profilePath, ex);
            return null;
        }
    }

    /// <summary>
    /// Şablon klasörü göreli verildiyse önce uygulama kökünde (Docker: /app), sonra repo kökünde (dotnet run) aranır.
    /// </summary>
    private string? ResolveTemplateDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (Path.IsPathRooted(path))
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;

        var contentRoot = environment.ContentRootPath;
        string[] candidates = [Path.Combine(contentRoot, path), Path.Combine(contentRoot, "..", "..", path)];
        return candidates.Where(Directory.Exists).Select(Path.GetFullPath).FirstOrDefault();
    }

    // Profil kimliği klasör adıdır; yol içinde kullanıldığı için yalnızca güvenli karakterlere izin veriyoruz.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SafeId();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mimari profili okunamadı: {Path}")]
    private static partial void LogInvalidProfile(ILogger logger, string path, Exception exception);

    private sealed class ProfileFile
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? TemplateDirectory { get; set; }
        public string? SourceName { get; set; }
        public List<string>? NewProject { get; set; }
        public List<string>? TestProjects { get; set; }
        public List<string>? Examples { get; set; }
        public List<ExtraExampleFile>? ExtraExamples { get; set; }
    }

    private sealed class ExtraExampleFile
    {
        public string? File { get; set; }
        public string? Path { get; set; }
    }
}
