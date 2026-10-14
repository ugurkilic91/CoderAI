using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DevAgents.Application.Context;

namespace DevAgents.Application.Agents.Commands.RunAgentPipeline;

/// <summary>
/// Kullanıcının geliştirme isteği (genelde Türkçe) ve isteğe bağlı proje bağlamı.
/// Ajan akışını çalıştırır ve ilerlemeyi olay akışı (stream) olarak döndürür; son olay "final" olayıdır.
/// </summary>
public record RunAgentPipelineCommand : IStreamRequest<PipelineEvent>
{
    public string Question { get; init; } = "";

    /// <summary>Proje klasörü. Yeni proje oluşturulurken (<see cref="NewProjectName"/>) projenin açılacağı üst klasördür.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Kullanıcının ek notları / kuralları (Türkçe olabilir).</summary>
    public string? ExtraContext { get; init; }

    public IReadOnlyList<InlineFile>? Files { get; init; }

    public IReadOnlyList<HistoryItem>? History { get; init; }

    public RunOptions? Options { get; init; }

    /// <summary>Kodun uyacağı mimari profilinin kimliği (örn. "clean-architecture"); boşsa projedeki mevcut düzen izlenir.</summary>
    public string? Architecture { get; init; }

    /// <summary>
    /// true: üretilen kod proje klasörüne yazılır, Docker sandbox'ta derlenir ve test edilir; hatalar ajanlara geri verilip
    /// düzelttirilir. Yazmadan önce projenin anlık görüntüsü alınır, değişiklikler geri alınabilir.
    /// </summary>
    public bool ApplyChanges { get; init; }

    /// <summary>Verilirse seçilen mimarinin şablonundan bu adla yeni bir proje oluşturulur ve kod onun içine yazılır.</summary>
    public string? NewProjectName { get; init; }
}

public class RunAgentPipelineCommandHandler : IStreamRequestHandler<RunAgentPipelineCommand, PipelineEvent>
{
    private readonly IAgentPipeline _pipeline;

    public RunAgentPipelineCommandHandler(IAgentPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async IAsyncEnumerable<PipelineEvent> Handle(
        RunAgentPipelineCommand request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Ajan motoru ilerlemeyi geri çağırımla (callback) bildiriyor, MediatR ise IAsyncEnumerable bekliyor.
        // Köprüyü bir kanal kuruyor: motor olayları kanala yazar, biz kanaldan okuyup sırayla dışarı veririz.
        var events = Channel.CreateUnbounded<PipelineEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var run = RunPipelineAsync(request, events.Writer, cancellationToken);

        await foreach (var pipelineEvent in events.Reader.ReadAllAsync(cancellationToken))
            yield return pipelineEvent;

        await run;
    }

    private async Task RunPipelineAsync(RunAgentPipelineCommand request, ChannelWriter<PipelineEvent> writer, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pipeline.RunAsync(request, e => writer.WriteAsync(e, cancellationToken).AsTask(), cancellationToken);
            await writer.WriteAsync(PipelineEvent.Completed(result), cancellationToken);
            writer.Complete();
        }
        catch (Exception ex)
        {
            // Hata kanala iletilir; okuyan taraf (uç nokta) bunu yakalayıp istemciye "error" olayı olarak gönderir.
            writer.Complete(ex);
        }
    }
}
