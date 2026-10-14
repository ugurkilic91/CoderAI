namespace DevAgents.Application.Context.Queries.PreviewContext;

/// <summary>Bir soru için ajanlara hangi bağlamın gideceğini gösterir (arayüzdeki "Bağlamı önizle" düğmesi).</summary>
public record PreviewContextQuery : IRequest<ContextPreviewDto>
{
    public string? ProjectPath { get; init; }

    public IReadOnlyList<InlineFile>? Files { get; init; }

    public string? Question { get; init; }
}

public class PreviewContextQueryHandler : IRequestHandler<PreviewContextQuery, ContextPreviewDto>
{
    private readonly IProjectContextBuilder _contextBuilder;

    public PreviewContextQueryHandler(IProjectContextBuilder contextBuilder)
    {
        _contextBuilder = contextBuilder;
    }

    public Task<ContextPreviewDto> Handle(PreviewContextQuery request, CancellationToken cancellationToken)
    {
        // Ajan akışındaki bağlam adımının aynısını, model çağırmadan çalıştırıyoruz.
        var context = _contextBuilder.Build(request.ProjectPath, request.Files, request.Question ?? "");
        return Task.FromResult(ContextPreviewDto.From(context));
    }
}

/// <summary>Ajanların göreceği bağlamın özeti.</summary>
public record ContextPreviewDto(string Overview, IReadOnlyList<string> Included, int TotalFiles, int Chars, string? Warning)
{
    public static ContextPreviewDto From(ProjectContext context) =>
        new(context.Overview, context.Included, context.TotalFiles, context.Chars, context.Warning);
}
