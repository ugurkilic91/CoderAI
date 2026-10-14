namespace DevAgents.Infrastructure.Sandbox;

/// <summary>Derleme/test ortamının ayarları; "Sandbox" bölümünden okunur.</summary>
internal sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    public const string DockerMode = "Docker";
    public const string ProcessMode = "Process";

    /// <summary>
    /// "Docker" (önerilen): her komut ayrı, kaynakları sınırlı bir konteynerde; derleme ve testler ağ kapalı çalışır.
    /// "Process": dotnet doğrudan sunucuda çalışır (Docker yoksa; üretilen kod DevAgents'in yetkileriyle çalışır).
    /// </summary>
    public string Mode { get; set; } = DockerMode;

    /// <summary>Sandbox imajı; .NET SDK içermeli.</summary>
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";

    public string Memory { get; set; } = "4g";

    public string Cpus { get; set; } = "2";

    public int PidsLimit { get; set; } = 1024;

    /// <summary>
    /// Konteynerin hangi kullanıcıyla çalışacağı. "auto": proje klasörünün sahibi (oluşan dosyalar root'a ait olmasın);
    /// boş: Docker'ın varsayılanı; ya da "1000:1000" gibi açık değer.
    /// </summary>
    public string User { get; set; } = "auto";

    public int RestoreTimeoutMinutes { get; set; } = 15;

    public int BuildTimeoutMinutes { get; set; } = 10;

    public int TestTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// DevAgents kendisi bir konteynerde çalışıyorsa: konteynerdeki yol → ana makinedeki yol.
    /// Sandbox konteynerlerini ana makinenin Docker'ı başlattığı için bağlamalar ana makine yollarıyla yapılmalıdır.
    /// </summary>
    public List<PathMapping> PathMappings { get; set; } = [];
}

public sealed class PathMapping
{
    public string Container { get; set; } = "";

    public string Host { get; set; } = "";
}
