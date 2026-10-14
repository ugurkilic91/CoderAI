namespace DevAgents.Application.Common.Exceptions;

/// <summary>Model sunucusuna (Ollama) ulaşılamadı ya da sunucu hata döndürdü. Web katmanı 502 döndürür.</summary>
public class LanguageModelException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Model, ayarlanan süre içinde cevabını bitiremedi. Web katmanı 504 döndürür.</summary>
public sealed class LanguageModelTimeoutException(string message) : LanguageModelException(message);
