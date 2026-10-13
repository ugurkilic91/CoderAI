using Microsoft.AspNetCore.Diagnostics;

namespace DevAgents.Api.Hosting;

/// <summary>
/// Turns exceptions thrown by endpoints into problem details responses whose "detail" is the error message
/// (the web UI displays it). Streaming endpoints report errors as events instead.
/// </summary>
internal sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = ApiErrors.StatusCodeFor(exception, httpContext.RequestAborted);
        httpContext.Response.StatusCode = status;
        if (status == StatusCodes.Status499ClientClosedRequest)
            return true; // nobody is listening any more

        if (status >= StatusCodes.Status500InternalServerError)
            LogRequestFailed(logger, httpContext.Request.Method, httpContext.Request.Path, exception);

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Detail = exception.Message }
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Method} {Path} failed")]
    private static partial void LogRequestFailed(ILogger logger, string method, PathString path, Exception exception);
}
