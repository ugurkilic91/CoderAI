using DevAgents.Api.Hosting;
using DevAgents.Api.OpenAi;
using DevAgents.Application.Chat;

namespace DevAgents.Api.Endpoints;

/// <summary>
/// OpenAI-compatible chat completions, so editor plugins such as Continue can use the pipeline as a model
/// (model id <see cref="ChatCompletionWriter.ModelId"/>). Only the last message is used: no project context or history.
/// </summary>
internal static class OpenAiCompatibleEndpoints
{
    public static IEndpointRouteBuilder MapOpenAiCompatibleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", CompleteAsync);
        return app;
    }

    private static async Task CompleteAsync(HttpContext httpContext, IAgentPipeline pipeline, TimeProvider timeProvider)
    {
        var cancellationToken = httpContext.RequestAborted;
        var completionRequest = await ChatCompletionRequest.ReadAsync(httpContext.Request.Body, cancellationToken);
        if (completionRequest is null || string.IsNullOrWhiteSpace(completionRequest.Prompt))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync("Prompt/Message bulunamadı.", cancellationToken);
            return;
        }

        var request = new ChatRequest(completionRequest.Prompt);
        var writer = new ChatCompletionWriter(httpContext.Response, timeProvider);
        if (completionRequest.Stream)
            await StreamAsync(pipeline, request, writer, cancellationToken);
        else
            await RespondAsync(pipeline, request, writer, cancellationToken);
    }

    private static async Task StreamAsync(IAgentPipeline pipeline, ChatRequest request, ChatCompletionWriter writer, CancellationToken cancellationToken)
    {
        writer.StartStream();
        try
        {
            // The agents' live output is streamed as it is generated, each stage announced by a quote line.
            var result = await pipeline.RunAsync(request, pipelineEvent => pipelineEvent.Type switch
            {
                PipelineEventTypes.Token when !string.IsNullOrEmpty(pipelineEvent.Text) =>
                    writer.WriteChunkAsync(pipelineEvent.Text, null, cancellationToken),
                PipelineEventTypes.StageStart =>
                    writer.WriteChunkAsync($"\n\n> 🤖 **{pipelineEvent.Text ?? pipelineEvent.Stage}** çalışıyor...\n\n", null, cancellationToken),
                _ => Task.CompletedTask
            }, cancellationToken);

            // ...followed by the final report.
            if (!string.IsNullOrEmpty(result.FinalMarkdown))
                await writer.WriteChunkAsync(result.FinalMarkdown, null, cancellationToken);
            await writer.WriteChunkAsync(null, "stop", cancellationToken);
            await writer.WriteDoneAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected.
        }
        catch (Exception ex)
        {
            await writer.WriteChunkAsync($"\n\n[Hata]: {ex.Message}", "stop", cancellationToken);
            await writer.WriteDoneAsync(cancellationToken);
        }
    }

    private static async Task RespondAsync(IAgentPipeline pipeline, ChatRequest request, ChatCompletionWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            var result = await pipeline.RunAsync(request, static _ => Task.CompletedTask, cancellationToken);
            await writer.WriteCompletionAsync(result.FinalMarkdown, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected.
        }
        catch (Exception ex)
        {
            await writer.WriteErrorAsync(ApiErrors.StatusCodeFor(ex, cancellationToken), ex.Message, cancellationToken);
        }
    }
}
