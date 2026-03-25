using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ordering.API.Common.Exceptions;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Exception occurred while processing the request: {Message}", exception.Message);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
        Activity.Current?.AddException(exception);

        int statusCode;
        string title;
        string detail;
        string type;
        switch (exception)
        {
            case ApplicationException:
                statusCode = StatusCodes.Status400BadRequest;
                title = "Bad Request";
                detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "The request was invalid.";
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
                break;
            case TimeoutException:
                statusCode = StatusCodes.Status408RequestTimeout;
                title = "Request Timeout";
                detail = _environment.IsDevelopment()
                    ? $"{httpContext.Request.Method} {httpContext.Request.Path} {httpContext.Request.QueryString}".Trim()
                    : "The request timed out.";
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.9";
                break;
            default:
                statusCode = StatusCodes.Status500InternalServerError;
                title = "Internal Server Error";
                detail = "An error occurred while processing the request.";
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
                break;
        }

        httpContext.Response.StatusCode = statusCode;
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = type,
                Title = title,
                Detail = detail
            }
        });
    }
}
