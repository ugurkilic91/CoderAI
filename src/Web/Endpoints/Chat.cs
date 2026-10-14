using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DevAgents.Web.Endpoints;

/// <summary>Ajan akışını çalıştıran uç noktalar.</summary>
public class Chat : IEndpointGroup
{
    public static string? RoutePrefix => "/api/chat";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(RunAgentPipeline);
        groupBuilder.MapPost(StreamAgentPipeline, "stream");
    }

    [EndpointSummary("Ajan akışını çalıştırır, sonucu tek seferde döndürür")]
    [EndpointDescription("Betikler ve diğer araçlar için. İlerleme olayları gönderilmez; cevap akışın sonucudur.")]
    public static async Task<Ok<PipelineResult>> RunAgentPipeline(ISender sender, RunAgentPipelineCommand command, CancellationToken cancellationToken)
    {
        // Aynı akışı çalıştırıp yalnızca son ("final") olaydaki sonucu alıyoruz.
        PipelineResult? result = null;
        await foreach (var pipelineEvent in sender.CreateStream(command, cancellationToken))
        {
            if (pipelineEvent is { Type: PipelineEventTypes.Final, Data: PipelineResult final })
                result = final;
        }
        return TypedResults.Ok(Guard.Against.Null(result));
    }

    [EndpointSummary("Ajan akışını çalıştırır, ilerlemeyi NDJSON olarak akıtır")]
    [EndpointDescription("Satır başına bir olay: stage_start, token, stage_end, plan, final, error. Web arayüzü ve VS Code eklentisi bunu kullanır.")]
    public static async Task StreamAgentPipeline(ISender sender, RunAgentPipelineCommand command, HttpContext httpContext, IOptions<JsonOptions> jsonOptions)
    {
        var cancellationToken = httpContext.RequestAborted;
        var events = NdjsonStream.Open(httpContext.Response, jsonOptions.Value.SerializerOptions);
        try
        {
            await foreach (var pipelineEvent in sender.CreateStream(command, cancellationToken))
                await events.WriteAsync(pipelineEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // İstemci bağlantıyı kesti.
        }
        catch (Exception ex)
        {
            // Akışı okuyan istemciler durum kodu değil olay okur: hatayı "error" olayı olarak gönderiyoruz.
            await events.TryWriteAsync(PipelineEvent.Failed(ex.Message), cancellationToken);
        }
    }
}
