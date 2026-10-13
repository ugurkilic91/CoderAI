using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Options;
using DevAgents.Domain.Agents;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.SystemInfo;

internal sealed class SystemInfoService(
    ILanguageModelClient languageModel,
    IAgentModelResolver modelResolver,
    IWorkspaceFileSystem workspace,
    IOptionsMonitor<PipelineOptions> pipelineOptions,
    IOptionsMonitor<WorkspaceOptions> workspaceOptions) : ISystemInfoService
{
    public async Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var models = await languageModel.ListModelsAsync(cancellationToken);
            return new HealthStatus(true, languageModel.Endpoint, models.Count, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthStatus(false, languageModel.Endpoint, ModelCount: 0, ex.Message);
        }
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        languageModel.ListModelsAsync(cancellationToken);

    public ConfigurationSnapshot GetConfiguration() => new(
        languageModel.Endpoint,
        [.. Enum.GetValues<AgentRole>().Select(role => modelResolver.Resolve(role))],
        pipelineOptions.CurrentValue,
        workspaceOptions.CurrentValue);

    public WorkspaceProjects ListProjects()
    {
        var roots = workspaceOptions.CurrentValue.AllowedRoots;
        return new WorkspaceProjects(roots, workspace.ListProjects(roots));
    }
}
