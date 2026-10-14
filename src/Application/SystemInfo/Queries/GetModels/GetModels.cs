using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Application.SystemInfo.Queries.GetModels;

/// <summary>Model sunucusunda yüklü modeller (arayüzdeki model seçim listeleri için).</summary>
public record GetModelsQuery : IRequest<IReadOnlyList<string>>;

public class GetModelsQueryHandler : IRequestHandler<GetModelsQuery, IReadOnlyList<string>>
{
    private readonly ILanguageModelClient _languageModel;

    public GetModelsQueryHandler(ILanguageModelClient languageModel)
    {
        _languageModel = languageModel;
    }

    public Task<IReadOnlyList<string>> Handle(GetModelsQuery request, CancellationToken cancellationToken) =>
        _languageModel.ListModelsAsync(cancellationToken);
}
