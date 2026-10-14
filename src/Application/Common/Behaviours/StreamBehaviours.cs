using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValidationException = DevAgents.Application.Common.Exceptions.ValidationException;

namespace DevAgents.Application.Common.Behaviours;

// MediatR'da akış (IStreamRequest) istekleri normal pipeline behavior'larından geçmez.
// Ajan akışı bir stream isteği olduğu için doğrulama ve hata loglamanın stream karşılıkları burada.

/// <summary>Akış başlamadan önce isteği doğrular; hata varsa ilk olaydan önce <see cref="ValidationException"/> fırlar.</summary>
public class StreamValidationBehaviour<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public StreamValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RequestValidation.ValidateAsync(_validators, request, cancellationToken);

        await foreach (var item in next().WithCancellation(cancellationToken))
            yield return item;
    }
}

/// <summary>Akış sırasında beklenmeyen bir hata olursa loglayıp yeniden fırlatır.</summary>
public class StreamUnhandledExceptionBehaviour<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private readonly ILogger<TRequest> _logger;

    public StreamUnhandledExceptionBehaviour(ILogger<TRequest> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // C#'ta catch bloğu olan bir try içinde "yield return" yazılamadığı için öğeleri elle ilerletiyoruz.
        await using var items = next().GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            TResponse current;
            try
            {
                if (!await items.MoveNextAsync())
                    yield break;
                current = items.Current;
            }
            catch (Exception ex) when (ex is not ValidationException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "DevAgents akışında beklenmeyen hata: {Name}", typeof(TRequest).Name);
                throw;
            }

            yield return current;
        }
    }
}
