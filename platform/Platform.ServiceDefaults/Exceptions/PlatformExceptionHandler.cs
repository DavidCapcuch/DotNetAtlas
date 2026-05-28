using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults.Exceptions;

/// <summary>
/// Platform-wide catch-all <see cref="IExceptionHandler"/> for unhandled CLR exceptions
/// reaching the HTTP pipeline. Auto-registered by
/// <see cref="WebApplicationBuilderExtensions.AddServiceDefaults"/> and wired into
/// the pipeline (prepending <c>UseExceptionHandler</c>) by
/// <see cref="ExceptionHandlerStartupFilter"/>; BCs do not need to call
/// <c>app.UseExceptionHandler()</c> in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// <para>Behaviour: every unhandled exception becomes a 500 RFC 9457 ProblemDetails
/// response. The <c>Detail</c> field carries <c>Exception.Message</c> in
/// Development / Testing for debuggability, and a generic redacted string in
/// deployed environments to prevent information leak.</para>
/// <para>The handler does NOT branch on exception type. Status-mapping of
/// expected failure modes belongs in <c>Platform.Api.SendErrorResponseAsync</c> via
/// the typed <c>DomainError</c> hierarchy (<c>Result.Fail</c>); CLR exceptions
/// reaching this handler are by definition unexpected (bug path), and giving them
/// distinct status codes encourages clients to treat them as expected.</para>
/// <para>If a BC needs a specific exception type mapped to a non-500 status, it
/// can register its own <see cref="IExceptionHandler"/> before this one — ASP.NET
/// tries handlers in registration order and the first to return <c>true</c> wins.</para>
/// </remarks>
internal sealed class PlatformExceptionHandler : IExceptionHandler
{
    private const string RedactedDetail = "An error occurred while processing the request.";
    private const string ProblemTypeUri = "https://tools.ietf.org/html/rfc9110#section-15.6.1";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<PlatformExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public PlatformExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<PlatformExceptionHandler> logger,
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
        _logger.LogError(
            exception,
            "Unhandled exception: {ExceptionType}",
            exception.GetType().Name);

        Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
        Activity.Current?.AddException(exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var detail = _environment.IsDeployedEnvironment() ? RedactedDetail : exception.Message;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = ProblemTypeUri,
                Title = "Internal Server Error",
                Detail = detail,
            },
        });
    }
}
