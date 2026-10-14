using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Dil modeli sunucusuyla (Ollama) sohbet. Uygulaması Infrastructure katmanındadır.</summary>
public interface ILanguageModelClient
{
    /// <summary>Model sunucusunun adresi; sağlık ve ayar ekranlarında gösterilir.</summary>
    string Endpoint { get; }

    /// <summary>Sunucuda yüklü modellerin adları, sıralı.</summary>
    /// <exception cref="LanguageModelException">Sunucuya ulaşılamadı ya da hata döndü.</exception>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="agent"/>'ın modeline bir sistem + kullanıcı mesajı gönderir ve cevabı akış hâlinde alır;
    /// her parçayı (token) <paramref name="onToken"/>'a iletir. Cevabın tamamını işlenmemiş hâliyle döndürür.
    /// </summary>
    /// <exception cref="LanguageModelException">Çağrı başarısız oldu ya da zaman aşımına uğradı.</exception>
    Task<string> ChatAsync(
        AgentModel agent,
        string systemPrompt,
        string userMessage,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken);
}
