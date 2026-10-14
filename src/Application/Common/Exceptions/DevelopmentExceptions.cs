namespace DevAgents.Application.Common.Exceptions;

/// <summary>Derleme/test ortamı (Docker) kullanılamıyor ya da bir komut çalıştırılamadı. Web katmanı 503 döndürür.</summary>
public class SandboxException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Değişiklik takibi (yedek) yapılamadı; güvenlik için projeye hiçbir şey yazılmaz.</summary>
public class ChangeTrackingException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Aynı projede başka bir geliştirme görevi sürüyor. Web katmanı 409 döndürür.</summary>
public class ProjectBusyException(string projectPath)
    : Exception($"Bu projede şu an başka bir geliştirme görevi çalışıyor: {projectPath}. Bitmesini bekleyin.");
