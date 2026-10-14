using DevAgents.Application.Agents.Development;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Domain.ValueObjects;

namespace DevAgents.Application.Changes.Commands.RevertChanges;

/// <summary>
/// Ajanların projede yaptığı değişiklikleri geri alır: proje, geliştirmeden önce alınan anlık görüntüye döner
/// (sonradan eklenen dosyalar silinir, değişenler eski hâline gelir).
/// </summary>
public record RevertChangesCommand : IRequest<IReadOnlyList<FileChange>>
{
    public string ProjectPath { get; init; } = "";

    /// <summary>Geliştirme sonucundaki anlık görüntü kimliği (development.snapshot).</summary>
    public string Snapshot { get; init; } = "";
}

public class RevertChangesCommandHandler : IRequestHandler<RevertChangesCommand, IReadOnlyList<FileChange>>
{
    private readonly IWorkspaceFileSystem _workspace;
    private readonly IChangeTracker _changeTracker;
    private readonly ProjectLocks _locks;

    public RevertChangesCommandHandler(IWorkspaceFileSystem workspace, IChangeTracker changeTracker, ProjectLocks locks)
    {
        _workspace = workspace;
        _changeTracker = changeTracker;
        _locks = locks;
    }

    public async Task<IReadOnlyList<FileChange>> Handle(RevertChangesCommand request, CancellationToken cancellationToken)
    {
        var projectRoot = _workspace.ResolveProjectPath(request.ProjectPath);
        // Geri alma da bir yazma işi: projede süren bir geliştirme varsa beklemesi gerekir.
        using var projectLock = _locks.Acquire(projectRoot);
        return await _changeTracker.RevertAsync(projectRoot, request.Snapshot, cancellationToken);
    }
}
