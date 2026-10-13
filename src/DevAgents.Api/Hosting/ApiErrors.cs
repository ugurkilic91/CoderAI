using DevAgents.Application.Common.Exceptions;

namespace DevAgents.Api.Hosting;

internal static class ApiErrors
{
    /// <summary>The HTTP status code that reports <paramref name="exception"/>.</summary>
    public static int StatusCodeFor(Exception exception, CancellationToken requestAborted) => exception switch
    {
        InvalidRequestException => StatusCodes.Status400BadRequest,
        BadHttpRequestException badRequest => badRequest.StatusCode,
        LanguageModelTimeoutException => StatusCodes.Status504GatewayTimeout,
        LanguageModelException => StatusCodes.Status502BadGateway,
        OperationCanceledException when requestAborted.IsCancellationRequested => StatusCodes.Status499ClientClosedRequest,
        _ => StatusCodes.Status500InternalServerError
    };
}
