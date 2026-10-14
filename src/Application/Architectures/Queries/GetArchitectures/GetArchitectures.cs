using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Application.Architectures.Queries.GetArchitectures;

/// <summary>Seçilebilir mimari profilleri (arayüzdeki "Mimari" listesi).</summary>
public record GetArchitecturesQuery : IRequest<IReadOnlyList<ArchitectureDto>>;

public class GetArchitecturesQueryHandler : IRequestHandler<GetArchitecturesQuery, IReadOnlyList<ArchitectureDto>>
{
    private readonly IArchitectureCatalog _architectures;

    public GetArchitecturesQueryHandler(IArchitectureCatalog architectures)
    {
        _architectures = architectures;
    }

    public Task<IReadOnlyList<ArchitectureDto>> Handle(GetArchitecturesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchitectureDto> list =
        [
            .. _architectures.GetAll().Select(profile =>
                new ArchitectureDto(profile.Id, profile.Name, profile.Description, profile.CanCreateProjects(), profile.TestProjects))
        ];
        return Task.FromResult(list);
    }
}

/// <param name="CanCreateProjects">Bu mimariyle yeni proje oluşturulabilir mi (şablonu tanımlı mı).</param>
/// <param name="TestProjects">Uygulama modunda çalıştırılan test projeleri.</param>
public record ArchitectureDto(string Id, string Name, string Description, bool CanCreateProjects, IReadOnlyList<string> TestProjects);
