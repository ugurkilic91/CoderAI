using Microsoft.Extensions.Logging;
using ValidationException = DevAgents.Application.Common.Exceptions.ValidationException;

namespace DevAgents.Application.Common.Behaviours;

/// <summary>
/// İşleyicide beklenmeyen bir hata olursa loglayıp yeniden fırlatır.
/// Doğrulama hataları loglanmaz: onlar kullanıcının düzelteceği, beklenen hatalardır.
/// </summary>
public class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;

    public UnhandledExceptionBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not ValidationException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "DevAgents isteğinde beklenmeyen hata: {Name}", typeof(TRequest).Name);
            throw;
        }
    }
}
