using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DevAgents.Application.Common.Behaviours;

/// <summary>Yarım saniyeden uzun süren komut/sorguları uyarı olarak loglar (örneğin Ollama yavaş cevap verdiğinde).</summary>
public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const long SlowRequestMilliseconds = 500;

    private readonly Stopwatch _timer;
    private readonly ILogger<TRequest> _logger;

    public PerformanceBehaviour(ILogger<TRequest> logger)
    {
        _timer = new Stopwatch();
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _timer.Start();
        var response = await next();
        _timer.Stop();

        if (_timer.ElapsedMilliseconds > SlowRequestMilliseconds)
            _logger.LogWarning("DevAgents uzun süren istek: {Name} ({ElapsedMilliseconds} ms)",
                typeof(TRequest).Name, _timer.ElapsedMilliseconds);

        return response;
    }
}
