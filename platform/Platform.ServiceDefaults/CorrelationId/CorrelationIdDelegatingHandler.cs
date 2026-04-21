using System.Diagnostics;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that copies the ambient correlation id onto every
/// outgoing <see cref="HttpRequestMessage"/>. Resolves the value from the ambient
/// <see cref="Activity.Current"/> tag (<see cref="CorrelationIdContextKeys.ActivityTagName"/>),
/// so it works uniformly in HTTP request scopes, Kafka consumers, and background workers.
/// Never overwrites an explicit header set by the caller.
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(CorrelationIdContextKeys.HttpHeaderName))
        {
            if (Activity.Current?.GetTagItem(CorrelationIdContextKeys.ActivityTagName) is string correlationId
                && !string.IsNullOrEmpty(correlationId))
            {
                request.Headers.TryAddWithoutValidation(CorrelationIdContextKeys.HttpHeaderName, correlationId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
