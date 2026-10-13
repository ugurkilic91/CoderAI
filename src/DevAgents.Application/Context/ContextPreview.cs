namespace DevAgents.Application.Context;

/// <summary>Asks which context the agents would receive for a question.</summary>
public sealed record ContextPreviewRequest(string? ProjectPath, IReadOnlyList<InlineFile>? Files, string? Question);

/// <summary>The context the agents would receive (the web UI's "Bağlamı önizle" dialog).</summary>
public sealed record ContextPreview(string Overview, IReadOnlyList<string> Included, int TotalFiles, int Chars, string? Warning)
{
    public static ContextPreview From(ProjectContext context) =>
        new(context.Overview, context.Included, context.TotalFiles, context.Chars, context.Warning);
}
