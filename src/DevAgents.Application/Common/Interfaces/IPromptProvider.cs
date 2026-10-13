namespace DevAgents.Application.Common.Interfaces;

/// <summary>Supplies the agents' system prompts.</summary>
public interface IPromptProvider
{
    /// <summary>Returns the system prompt with the given name (prompts/&lt;name&gt;.md).</summary>
    string GetPrompt(string name);
}
