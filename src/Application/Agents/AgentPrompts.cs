namespace DevAgents.Application.Agents;

/// <summary>Ajanların sistem prompt'larının adları (prompts/&lt;ad&gt;.md).</summary>
internal static class AgentPrompts
{
    public const string TranslatorToEnglish = "translator-to-en";
    public const string Router = "router";
    public const string Answerer = "answerer";
    public const string Analyzer = "analyzer";
    public const string Coder = "coder";
    public const string Tester = "tester";
    public const string Reviewer = "reviewer";
    public const string TranslatorToTurkish = "translator-to-tr";

    /// <summary>Derleme ve test hatalarını düzelten ajan (kodlama ajanının modeliyle çalışır).</summary>
    public const string Fixer = "fixer";
}
