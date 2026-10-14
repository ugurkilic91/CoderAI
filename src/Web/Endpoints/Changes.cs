using DevAgents.Application.Changes.Commands.RevertChanges;
using DevAgents.Domain.ValueObjects;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DevAgents.Web.Endpoints;

/// <summary>Ajanların projede yaptığı değişiklikler.</summary>
public class Changes : IEndpointGroup
{
    public static string? RoutePrefix => "/api/changes";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(RevertChanges, "revert");
    }

    [EndpointSummary("Değişiklikleri geri alır")]
    [EndpointDescription("Projeyi, geliştirmeden önce alınan anlık görüntüye döndürür ve geri alınan dosyaları listeler.")]
    public static async Task<Ok<IReadOnlyList<FileChange>>> RevertChanges(ISender sender, RevertChangesCommand command, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(command, cancellationToken));
}
