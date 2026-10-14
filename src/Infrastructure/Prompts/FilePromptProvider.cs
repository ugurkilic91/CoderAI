using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Infrastructure.Prompts;

/// <summary>
/// Sistem prompt'larını uygulamanın yanındaki prompts/ klasöründen okur. Dosyalar her çağrıda yeniden okunur:
/// Docker'da klasör repodan bağlandığı için düzenlemeler anında geçerli olur.
/// </summary>
internal sealed class FilePromptProvider : IPromptProvider
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "prompts");

    public string GetPrompt(string name)
    {
        var path = Path.Combine(_directory, name + ".md");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt dosyası bulunamadı: {path}", path);
        return File.ReadAllText(path);
    }
}
