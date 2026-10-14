namespace DevAgents.Application.Common.Interfaces;

/// <summary>Ajanların sistem prompt'larını sağlar (prompts/&lt;ad&gt;.md dosyaları).</summary>
public interface IPromptProvider
{
    string GetPrompt(string name);
}
