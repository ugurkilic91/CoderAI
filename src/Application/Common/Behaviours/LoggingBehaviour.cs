using MediatR.Pipeline;
using Microsoft.Extensions.Logging;

namespace DevAgents.Application.Common.Behaviours;

/// <summary>
/// Her komut/sorgu çalışmadan önce adını loglar.
/// Şablondan farkı: isteğin içeriği bilerek loglanmaz, çünkü istekler kaynak kod ve dosya içerikleri taşıyabilir.
/// </summary>
public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly ILogger _logger;

    public LoggingBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public Task Process(TRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DevAgents isteği: {Name}", typeof(TRequest).Name);
        return Task.CompletedTask;
    }
}
