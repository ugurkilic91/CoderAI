using System.Text;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Domain.Common;
using DevAgents.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace DevAgents.Infrastructure.Workspace;

/// <summary>Ajanların ürettiği dosyaları proje klasörüne yazar; proje dışına ve araç klasörlerine yazmaz.</summary>
internal sealed partial class FileSystemProjectWriter(ILogger<FileSystemProjectWriter> logger) : IProjectWriter
{
    // Bu klasörlere hiçbir zaman yazılmaz: git deposu ve derleme çıktıları.
    private static readonly string[] ForbiddenDirectories = [".git", "bin", "obj", ".vs", "node_modules"];

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<IReadOnlyList<FileChange>> WriteAsync(string projectRoot, IReadOnlyList<GeneratedFile> files, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(projectRoot);
        var changes = new List<FileChange>();
        foreach (var file in files)
        {
            // Güvenlik: yol temizlenir, proje kökünün altında kalmalı ve yasak klasörlere girmemeli.
            var relativePath = RelativePath.Normalize(file.Path);
            var target = relativePath is null ? null : Path.GetFullPath(Path.Combine(root, relativePath));
            if (relativePath is null || target is null || !IsUnder(target, root)
                || relativePath.Split('/').Any(segment => ForbiddenDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            {
                LogSkipped(logger, file.Path);
                continue;
            }

            var content = file.Content.EndsWith('\n') ? file.Content : file.Content + "\n";
            var existed = File.Exists(target);
            if (existed && await File.ReadAllTextAsync(target, cancellationToken) == content)
                continue; // içerik aynı: dosyaya dokunmuyoruz

            // Var olan dosya BOM ile kaydedilmişse (Visual Studio şablonları böyle) aynı kodlamayı koruyoruz.
            var encoding = existed && await HasBomAsync(target, cancellationToken) ? Utf8WithBom : Utf8WithoutBom;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content, encoding, cancellationToken);
            changes.Add(new FileChange(relativePath, existed ? FileChangeKind.Modified : FileChangeKind.Created));
        }
        return changes;
    }

    private static async Task<bool> HasBomAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[3];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(buffer, cancellationToken);
        return read == 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF;
    }

    private static bool IsUnder(string path, string root)
    {
        var separator = Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(root.TrimEnd(separator) + separator, comparison);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Güvensiz yol nedeniyle yazılmadı: {Path}")]
    private static partial void LogSkipped(ILogger logger, string path);
}
