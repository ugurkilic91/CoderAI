namespace DevAgents.Application.Context;

/// <summary>Ajanlara verilen, mimariyi anlatan bağlam: proje özeti ve isteğe en uygun dosyalar.</summary>
public sealed class ProjectContext
{
    /// <summary>Ne proje yolu ne de dosya verildiğinde kullanılan boş bağlam.</summary>
    public static ProjectContext None { get; } = new()
    {
        Overview = "(no project context was provided)",
        Warning = "Proje yolu veya dosya verilmedi; agent'lar genel bilgiyle çalışacak."
    };

    public string Overview { get; init; } = "";

    /// <summary>Seçilen dosyalar, <c>&lt;file path="..."&gt;</c> blokları olarak.</summary>
    public string FilesText { get; init; } = "";

    /// <summary>Seçilen dosyaların yolları, <see cref="FilesText"/> içindeki sırayla.</summary>
    public IReadOnlyList<string> Included { get; init; } = [];

    /// <summary>İndekslenen (seçimden önceki) dosya sayısı.</summary>
    public int TotalFiles { get; init; }

    public string? Warning { get; init; }

    /// <summary>Bağlamın ajan mesajlarındaki hâli.</summary>
    public string Text =>
        $"### PROJECT OVERVIEW\n{Overview}\n\n### RELEVANT PROJECT FILES\n{(FilesText.Length == 0 ? "(none)" : FilesText)}";

    public int Chars => Text.Length;

    public ContextInfo ToInfo() => new(TotalFiles, Included, Chars, Warning);
}

/// <summary>Ajanlara verilen bağlamın özeti (istemciye gönderilir).</summary>
public sealed record ContextInfo(int TotalFiles, IReadOnlyList<string> IncludedFiles, int Chars, string? Warning);
