namespace DevAgents.Application.Common.Models;

/// <summary>İzinli bir çalışma alanı kökü altındaki proje klasörü.</summary>
public sealed record WorkspaceProject(string Name, string Path);
