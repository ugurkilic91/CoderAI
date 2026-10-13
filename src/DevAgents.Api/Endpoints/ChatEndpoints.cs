using DevAgents.Application.Chat;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DevAgents.Api.Endpoints;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // Single JSON response (for scripts and other tools).
        app.MapPost("/api/chat", RunAsync);
        // NDJSON event stream (stage_start | token | stage_end | plan | final | error), used by the web chat and the VS Code extension.
        app.MapPost("/api/chat/stream", StreamAsync);
        return app;
    }

    private static async Task<Ok<PipelineResult>> RunAsync(ChatRequest request, IAgentPipeline pipeline, CancellationToken cancellationToken) =>
        TypedResults.Ok(await pipeline.RunAsync(request, static _ => Task.CompletedTask, cancellationToken));

    private static async Task StreamAsync(ChatRequest request, IAgentPipeline pipeline, HttpContext httpContext, IOptions<JsonOptions> jsonOptions)
    {
        var cancellationToken = httpContext.RequestAborted;
        var events = NdjsonStream.Open(httpContext.Response, jsonOptions.Value.SerializerOptions);
        try
        {
            var result = await pipeline.RunAsync(request, pipelineEvent => events.WriteAsync(pipelineEvent, cancellationToken), cancellationToken);
            await events.WriteAsync(PipelineEvent.Completed(result), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected.
        }
        catch (Exception ex)
        {
            // Clients of the stream read events, not status codes (the stream has usually started already).
            await events.TryWriteAsync(PipelineEvent.Failed(ex.Message), cancellationToken);
        }
    }
}
