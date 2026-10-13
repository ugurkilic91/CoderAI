using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;

namespace DevAgents.Application.SystemInfo;

/// <summary>Information about the running service: model server health, models, effective settings, workspace projects.</summary>
public interface ISystemInfoService
{
    /// <summary>Never throws because the model server is down; reports it instead.</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken);

    /// <exception cref="LanguageModelException">The model server cannot be reached.</exception>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>The settings in effect right now (configuration files are reloaded on change).</summary>
    ConfigurationSnapshot GetConfiguration();

    WorkspaceProjects ListProjects();
}

public sealed record HealthStatus(bool IsHealthy, string Endpoint, int ModelCount, string? Error);

/// <param name="Agents">Every agent's resolved model settings, in pipeline order.</param>
public sealed record ConfigurationSnapshot(
    string Endpoint,
    IReadOnlyList<AgentModel> Agents,
    PipelineOptions Pipeline,
    WorkspaceOptions Workspace);

/// <param name="Roots">The allowed workspace roots (empty: any local path may be used).</param>
public sealed record WorkspaceProjects(IReadOnlyList<string> Roots, IReadOnlyList<WorkspaceProject> Projects);
