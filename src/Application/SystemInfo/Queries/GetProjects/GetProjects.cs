using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.SystemInfo.Queries.GetProjects;

/// <summary>İzinli çalışma alanı köklerindeki proje klasörleri (arayüzdeki proje öneri listesi).</summary>
public record GetProjectsQuery : IRequest<ProjectsDto>;

public class GetProjectsQueryHandler : IRequestHandler<GetProjectsQuery, ProjectsDto>
{
    private readonly IWorkspaceFileSystem _workspace;
    private readonly IOptionsMonitor<WorkspaceOptions> _workspaceOptions;

    public GetProjectsQueryHandler(IWorkspaceFileSystem workspace, IOptionsMonitor<WorkspaceOptions> workspaceOptions)
    {
        _workspace = workspace;
        _workspaceOptions = workspaceOptions;
    }

    public Task<ProjectsDto> Handle(GetProjectsQuery request, CancellationToken cancellationToken)
    {
        var roots = _workspaceOptions.CurrentValue.AllowedRoots;
        return Task.FromResult(new ProjectsDto(roots, _workspace.ListProjects(roots)));
    }
}

/// <param name="Roots">İzinli kökler (boşsa makinedeki her yol kullanılabilir).</param>
public record ProjectsDto(IReadOnlyList<string> Roots, IReadOnlyList<WorkspaceProject> Projects);
