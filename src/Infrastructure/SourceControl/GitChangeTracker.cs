using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Domain.ValueObjects;
using DevAgents.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DevAgents.Infrastructure.SourceControl;

/// <summary>
/// Değişiklikleri, projenin dışında tutulan gizli bir git deposuyla izler:
/// &lt;DataDirectory&gt;/history/&lt;proje&gt;-&lt;özet&gt;.git. Komutlar "--git-dir/--work-tree" ile çalışır; böylece
/// projenin kendi .git klasörüne, dallarına ve geçmişine hiç dokunulmaz, git deposu olmayan projeler de izlenebilir.
/// </summary>
internal sealed class GitChangeTracker(IOptionsMonitor<StorageOptions> storage) : IChangeTracker
{
    // Derleme çıktıları ve araç klasörleri izlenmez (projenin .gitignore dosyaları da ayrıca dikkate alınır).
    private static readonly string[] Excludes =
        ["bin/", "obj/", "artifacts/", "node_modules/", ".vs/", ".idea/", "TestResults/", "*.user", ".DS_Store"];

    public async Task<string> SnapshotAsync(string projectRoot, string message, CancellationToken cancellationToken)
    {
        var repository = await EnsureRepositoryAsync(cancellationToken, projectRoot);
        await GitAsync(repository, projectRoot, cancellationToken, "add", "--all", ".");
        await GitAsync(repository, projectRoot, cancellationToken,
            "-c", "user.name=DevAgents", "-c", "user.email=devagents@localhost",
            "commit", "--allow-empty", "--quiet", "--no-verify", "-m", message);
        return (await GitAsync(repository, projectRoot, cancellationToken, "rev-parse", "HEAD")).Trim();
    }

    public async Task<IReadOnlyList<FileChange>> GetChangesAsync(string projectRoot, string snapshot, CancellationToken cancellationToken)
    {
        var repository = RepositoryFor(projectRoot);
        // Çalışma ağacının şimdiki hâlini dizine alıp anlık görüntüyle karşılaştırıyoruz.
        await GitAsync(repository, projectRoot, cancellationToken, "add", "--all", ".");
        var output = await GitAsync(repository, projectRoot, cancellationToken,
            "-c", "core.quotepath=false", "diff", "--cached", "--name-status", "--no-renames", snapshot);
        return ParseNameStatus(output);
    }

    public async Task<IReadOnlyList<FileChange>> RevertAsync(string projectRoot, string snapshot, CancellationToken cancellationToken)
    {
        var repository = RepositoryFor(projectRoot);
        if (!Directory.Exists(repository))
            throw new ChangeTrackingException($"Bu proje için kayıtlı bir anlık görüntü yok: {projectRoot}");

        var changes = await GetChangesAsync(projectRoot, snapshot, cancellationToken);
        var root = Path.GetFullPath(projectRoot);
        foreach (var change in changes)
        {
            if (change.Kind == FileChangeKind.Created)
            {
                // Anlık görüntüden sonra eklenen dosya: siliyoruz (boş kalan klasörleriyle birlikte).
                var path = Path.GetFullPath(Path.Combine(root, change.Path));
                if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
                {
                    File.Delete(path);
                    DeleteEmptyDirectories(Path.GetDirectoryName(path)!, root);
                }
            }
            else
            {
                // Değişen ya da silinen dosya: anlık görüntüdeki hâline döndürüyoruz.
                await GitAsync(repository, projectRoot, cancellationToken, "checkout", snapshot, "--", change.Path);
            }
        }

        await GitAsync(repository, projectRoot, cancellationToken, "add", "--all", ".");
        return changes;
    }

    private async Task<string> EnsureRepositoryAsync(CancellationToken cancellationToken, string projectRoot)
    {
        var repository = RepositoryFor(projectRoot);
        if (Directory.Exists(repository))
            return repository;

        Directory.CreateDirectory(repository);
        await RunGitAsync(["init", "--bare", "--quiet", repository], "init", cancellationToken);
        await File.WriteAllLinesAsync(Path.Combine(repository, "info", "exclude"), Excludes, cancellationToken);
        return repository;
    }

    // Aynı proje her zaman aynı gizli depoya gider: klasör adı + tam yolun özeti.
    private string RepositoryFor(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        var name = new string(Path.GetFileName(root).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return Path.Combine(storage.CurrentValue.ResolveDataDirectory(), "history", $"{name}-{hash}.git");
    }

    private static Task<string> GitAsync(string repository, string workTree, CancellationToken cancellationToken, params string[] arguments) =>
        RunGitAsync(["--git-dir", repository, "--work-tree", workTree, "-c", "safe.directory=*", "-c", "core.autocrlf=false", .. arguments],
            arguments.First(argument => !argument.StartsWith('-') && !argument.Contains('=')), cancellationToken);

    private static async Task<string> RunGitAsync(IReadOnlyList<string> arguments, string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        // Kullanıcıya şifre sormasın, sistem ayarlarındaki kancaları (hook) çalıştırmasın.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new ChangeTrackingException("git başlatılamadı.");
        }
        catch (Win32Exception ex)
        {
            throw new ChangeTrackingException("Değişiklik takibi (yedek) için git gerekli ama bulunamadı; git'i kurun.", ex);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new ChangeTrackingException($"Değişiklik takibi başarısız (git {command}): {(await error).Trim()}");
            return await output;
        }
    }

    // "A\tpath", "M\tpath", "D\tpath" satırları.
    private static List<FileChange> ParseNameStatus(string output) =>
    [
        .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new FileChange(parts[1].Trim(), parts[0].Trim() switch
            {
                "A" => FileChangeKind.Created,
                "D" => FileChangeKind.Deleted,
                _ => FileChangeKind.Modified
            }))
    ];

    private static void DeleteEmptyDirectories(string directory, string root)
    {
        while (directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory)!;
        }
    }
}
