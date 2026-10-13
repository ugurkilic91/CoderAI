using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Infrastructure.Prompts;

/// <summary>
/// Reads system prompts from the prompts/ folder next to the application. Files are read on every call,
/// so edits apply without a restart (Docker mounts the folder from the repository).
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
