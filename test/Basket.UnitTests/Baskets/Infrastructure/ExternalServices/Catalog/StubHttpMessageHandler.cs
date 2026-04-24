namespace Basket.UnitTests.Baskets.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Test-double <see cref="DelegatingHandler"/> whose <see cref="SendAsync"/>
/// delegates to a per-test <see cref="Func{T1, T2, TResult}"/>, recording the
/// last request URI + call count for assertions. Hand-rolled because
/// <c>HttpMessageHandler.SendAsync</c> is protected and cannot be substituted
/// via NSubstitute.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _behavior;

    public StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        _behavior = behavior;
    }

    public int CallCount { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    public string? LastRequestPathAndQuery => LastRequestUri?.PathAndQuery;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri;
        return _behavior(request, cancellationToken);
    }
}
