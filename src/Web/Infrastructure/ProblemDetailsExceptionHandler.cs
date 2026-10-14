using DevAgents.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevAgents.Web.Infrastructure;

/// <summary>
/// Uç noktalarda fırlayan hataları RFC 9110 uyumlu ProblemDetails cevaplarına çevirir.
/// "detail" alanına her zaman (Türkçe) hata mesajını yazıyoruz; web arayüzü bu alanı gösteriyor.
/// Akış uç noktaları hatayı kendileri yakalayıp "error" olayı olarak gönderdiği için buraya düşmez.
/// </summary>
public partial class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = StatusCodeFor(exception, httpContext.RequestAborted);
        httpContext.Response.StatusCode = statusCode;

        // İstemci bağlantıyı kesmiş: cevap yazmanın anlamı yok.
        if (statusCode == StatusCodes.Status499ClientClosedRequest)
            return true;

        // Beklenmeyen ve dış servis kaynaklı (5xx) hatalar loglanır; kullanıcı hataları (4xx) loglanmaz.
        if (statusCode >= StatusCodes.Status500InternalServerError)
            LogRequestFailed(_logger, httpContext.Request.Method, httpContext.Request.Path, exception);

        ProblemDetails problemDetails = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors)
            : new ProblemDetails();
        problemDetails.Status = statusCode;
        problemDetails.Detail = exception.Message;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    /// <summary>Bir hatayı hangi HTTP durum koduyla bildireceğimiz (OpenAI uyumlu uç nokta da bunu kullanır).</summary>
    public static int StatusCodeFor(Exception exception, CancellationToken requestAborted) => exception switch
    {
        ValidationException or ProjectPathException => StatusCodes.Status400BadRequest,
        BadHttpRequestException badRequest => badRequest.StatusCode,
        ProjectBusyException => StatusCodes.Status409Conflict,
        LanguageModelTimeoutException => StatusCodes.Status504GatewayTimeout,
        LanguageModelException => StatusCodes.Status502BadGateway,
        SandboxException or ChangeTrackingException => StatusCodes.Status503ServiceUnavailable,
        OperationCanceledException when requestAborted.IsCancellationRequested => StatusCodes.Status499ClientClosedRequest,
        _ => StatusCodes.Status500InternalServerError
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "{Method} {Path} başarısız oldu")]
    private static partial void LogRequestFailed(ILogger logger, string method, PathString path, Exception exception);
}
