using DevAgents.Application.Context.Queries.PreviewContext;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DevAgents.Web.Endpoints;

/// <summary>Proje bağlamı uç noktaları.</summary>
public class Context : IEndpointGroup
{
    public static string? RoutePrefix => "/api/context";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(PreviewContext, "preview");
    }

    [EndpointSummary("Ajanlara gidecek bağlamı gösterir")]
    [EndpointDescription("Model çağırmadan, akıştaki bağlam adımını çalıştırır: proje özeti ve seçilen dosyalar.")]
    public static async Task<Ok<ContextPreviewDto>> PreviewContext(ISender sender, PreviewContextQuery query) =>
        TypedResults.Ok(await sender.Send(query));
}
