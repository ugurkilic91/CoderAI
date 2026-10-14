using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Web.OpenAi;

namespace DevAgents.Web.Endpoints;

/// <summary>
/// OpenAI uyumlu sohbet uç noktası: Continue gibi editör eklentileri ajan akışını bir model gibi kullanabilsin
/// (model adı <see cref="ChatCompletionWriter.ModelId"/>). Yalnızca son mesaj kullanılır; proje bağlamı ve geçmiş gönderilmez.
/// </summary>
public class OpenAiCompatible : IEndpointGroup
{
    public static string? RoutePrefix => "/v1";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(CreateChatCompletion, "chat/completions");
    }

    [EndpointSummary("OpenAI uyumlu sohbet tamamlama")]
    [EndpointDescription("\"stream\": false gönderilmedikçe cevap server-sent events olarak akar.")]
    public static async Task CreateChatCompletion(HttpContext httpContext, ISender sender, TimeProvider timeProvider)
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

        var command = new RunAgentPipelineCommand { Question = completionRequest.Prompt };
        var writer = new ChatCompletionWriter(httpContext.Response, timeProvider);
        if (completionRequest.Stream)
            await StreamAsync(sender, command, writer, cancellationToken);
        else
            await RespondAsync(sender, command, writer, cancellationToken);
    }

    private static async Task StreamAsync(ISender sender, RunAgentPipelineCommand command, ChatCompletionWriter writer, CancellationToken cancellationToken)
    {
        writer.StartStream();
        try
        {
            // Ajanların canlı çıktısı üretildikçe akıyor; her aşama bir alıntı satırıyla duyuruluyor.
            PipelineResult? result = null;
            await foreach (var pipelineEvent in sender.CreateStream(command, cancellationToken))
            {
                switch (pipelineEvent)
                {
                    case { Type: PipelineEventTypes.Token, Text: { Length: > 0 } token }:
                        await writer.WriteChunkAsync(token, null, cancellationToken);
                        break;
                    case { Type: PipelineEventTypes.StageStart }:
                        await writer.WriteChunkAsync($"\n\n> 🤖 **{pipelineEvent.Text ?? pipelineEvent.Stage}** çalışıyor...\n\n", null, cancellationToken);
                        break;
                    case { Type: PipelineEventTypes.Final, Data: PipelineResult final }:
                        result = final;
                        break;
                }
            }

            // ... ardından nihai rapor.
            if (!string.IsNullOrEmpty(result?.FinalMarkdown))
                await writer.WriteChunkAsync(result.FinalMarkdown, null, cancellationToken);
            await writer.WriteChunkAsync(null, "stop", cancellationToken);
            await writer.WriteDoneAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // İstemci bağlantıyı kesti.
        }
        catch (Exception ex)
        {
            await writer.WriteChunkAsync($"\n\n[Hata]: {ex.Message}", "stop", cancellationToken);
            await writer.WriteDoneAsync(cancellationToken);
        }
    }

    private static async Task RespondAsync(ISender sender, RunAgentPipelineCommand command, ChatCompletionWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            PipelineResult? result = null;
            await foreach (var pipelineEvent in sender.CreateStream(command, cancellationToken))
            {
                if (pipelineEvent is { Type: PipelineEventTypes.Final, Data: PipelineResult final })
                    result = final;
            }
            await writer.WriteCompletionAsync(result?.FinalMarkdown ?? "", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // İstemci bağlantıyı kesti.
        }
        catch (Exception ex)
        {
            await writer.WriteErrorAsync(ProblemDetailsExceptionHandler.StatusCodeFor(ex, cancellationToken), ex.Message, cancellationToken);
        }
    }
}
