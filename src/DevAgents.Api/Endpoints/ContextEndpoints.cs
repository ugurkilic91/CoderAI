using DevAgents.Application.Context;

namespace DevAgents.Api.Endpoints;

internal static class ContextEndpoints
{
    public static IEndpointRouteBuilder MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        // Shows the context the agents would receive for a question ("Bağlamı önizle" in the web UI).
        app.MapPost("/api/context/preview", (ContextPreviewRequest request, IProjectContextBuilder contextBuilder) =>
            TypedResults.Ok(ContextPreview.From(contextBuilder.Build(request.ProjectPath, request.Files, request.Question ?? ""))));
        return app;
    }
}
