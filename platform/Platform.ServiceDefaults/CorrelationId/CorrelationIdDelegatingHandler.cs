using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that copies the ambient correlation id onto every
/// outgoing <see cref="HttpRequestMessage"/>. Resolves the value from <see cref="HttpContext.Items"/>
/// first, falling back to the ambient <see cref="Activity.Current"/> tag. Never overwrites an
/// explicit header set by the caller.
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CorrelationIdOptions _options;

    /// <summary>
    /// Initializes a new <see cref="CorrelationIdDelegatingHandler"/>.
    /// </summary>
    public CorrelationIdDelegatingHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptions<CorrelationIdOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(_options.HeaderName))
        {
            var correlationId = ResolveAmbient();
            if (!string.IsNullOrEmpty(correlationId))
            {
                request.Headers.TryAddWithoutValidation(_options.HeaderName, correlationId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string? ResolveAmbient()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null
            && httpContext.Items.TryGetValue(CorrelationIdContextKeys.HttpContextItemKey, out var stored)
            && stored is string fromItems
            && !string.IsNullOrEmpty(fromItems))
        {
            return fromItems;
        }

        return Activity.Current?.GetTagItem(CorrelationIdContextKeys.ActivityTagName) as string;
    }
}
