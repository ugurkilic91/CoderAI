namespace DevAgents.Services;

/// <summary>Loads agent system prompts from the prompts/ folder (read on each call, so edits apply without restart).</summary>
public sealed class PromptStore
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "prompts");

    public string Get(string name)
    {
        var path = Path.Combine(_dir, name + ".md");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt dosyası bulunamadı: {path}");
        return File.ReadAllText(path);
    }
}
