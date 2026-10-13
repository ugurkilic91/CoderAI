namespace DevAgents.Application.Common.Models;

/// <summary>A project folder under one of the allowed workspace roots.</summary>
public sealed record WorkspaceProject(string Name, string Path);
