using ValidationException = DevAgents.Application.Common.Exceptions.ValidationException;

namespace DevAgents.Application.Common.Behaviours;

/// <summary>
/// İşleyici çalışmadan önce isteğin tüm FluentValidation doğrulayıcılarını çalıştırır;
/// hata varsa <see cref="ValidationException"/> fırlatır ve işleyiciye hiç gidilmez.
/// </summary>
public class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        await RequestValidation.ValidateAsync(_validators, request, cancellationToken);
        return await next();
    }
}

/// <summary>Normal ve akış (stream) isteklerinin ortak doğrulama mantığı.</summary>
internal static class RequestValidation
{
    public static async Task ValidateAsync<TRequest>(
        IEnumerable<IValidator<TRequest>> validators, TRequest request, CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return;

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));

        var failures = validationResults
            .Where(r => r.Errors.Count != 0)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);
    }
}
