namespace DevAgents.Application.Common.Exceptions;

/// <summary>
/// Proje yolu kullanılamıyor: izin verilen kök dizinlerin dışında, geçersiz ya da klasör yok.
/// Doğrulayıcılar bu hatayı önceden yakalar; dosya sistemi katmanı da güvenlik için yeniden kontrol eder.
/// </summary>
public class ProjectPathException(string message) : Exception(message);
