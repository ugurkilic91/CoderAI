namespace DevAgents.Application.Context;

/// <summary>A file supplied by the client (chat UI attachment, VS Code workspace file or editor selection).</summary>
/// <param name="Pinned">Pinned files always reach the agents, even if they are not source files.</param>
public sealed record InlineFile(string Path, string Content, bool Pinned = false);
