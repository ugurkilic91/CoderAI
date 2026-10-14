using DevAgents.Application.Architectures.Queries.GetArchitectures;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DevAgents.Web.Endpoints;

/// <summary>Mimari profilleri: ajanların kod üretirken uyacağı mimariler.</summary>
public class Architectures : IEndpointGroup
{
    public static string? RoutePrefix => "/api/architectures";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetArchitectures);
    }

    [EndpointSummary("Seçilebilir mimariler")]
    [EndpointDescription("architectures/ klasöründeki profiller. Yeni bir mimari eklemek için yeni bir profil klasörü eklemek yeterlidir.")]
    public static async Task<Ok<IReadOnlyList<ArchitectureDto>>> GetArchitectures(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetArchitecturesQuery(), cancellationToken));
}
