using DevAgents.Application.Common.Interfaces;

namespace DevAgents.Application.SystemInfo.Queries.GetHealth;

/// <summary>Model sunucusuna (Ollama) ulaşılabiliyor mu ve kaç model yüklü.</summary>
public record GetHealthQuery : IRequest<HealthDto>;

public class GetHealthQueryHandler : IRequestHandler<GetHealthQuery, HealthDto>
{
    private readonly ILanguageModelClient _languageModel;

    public GetHealthQueryHandler(ILanguageModelClient languageModel)
    {
        _languageModel = languageModel;
    }

    public async Task<HealthDto> Handle(GetHealthQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var models = await _languageModel.ListModelsAsync(cancellationToken);
            return new HealthDto(true, _languageModel.Endpoint, models.Count, Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Sağlık kontrolü hata fırlatmaz; sorunu sonuç içinde bildirir.
            return new HealthDto(false, _languageModel.Endpoint, ModelCount: 0, ex.Message);
        }
    }
}

public record HealthDto(bool IsHealthy, string Endpoint, int ModelCount, string? Error);
