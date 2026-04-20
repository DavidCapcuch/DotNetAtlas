using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.CorrelationId;

namespace Platform.ServiceDefaults.UnitTests.CorrelationId;

public class CorrelationIdDelegatingHandlerTests
{
    private static readonly IOptions<CorrelationIdOptions> DefaultOptions = Options.Create(new CorrelationIdOptions());

    [Fact]
    public async Task SendAsync_WithAmbientHttpContextItemsValue_AddsHeaderToOutbound()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Items[CorrelationIdContextKeys.HttpContextItemKey] = correlationId;

        var capturing = new CapturingHandler();
        using var client = BuildClient(accessor, capturing);

        // Act
        _ = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest.Should().NotBeNull();
        capturing.LastRequest!.Headers.GetValues(CorrelationIdContextKeys.HttpHeaderName)
            .Should().ContainSingle().Which.Should().Be(correlationId);
    }

    [Fact]
    public async Task SendAsync_WithExistingOutboundHeader_DoesNotOverwrite()
    {
        // Arrange
        var explicitValue = Guid.CreateVersion7().ToString();
        var ambientValue = Guid.CreateVersion7().ToString();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Items[CorrelationIdContextKeys.HttpContextItemKey] = ambientValue;

        var capturing = new CapturingHandler();
        using var client = BuildClient(accessor, capturing);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping");
        request.Headers.Add(CorrelationIdContextKeys.HttpHeaderName, explicitValue);

        // Act
        _ = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest!.Headers.GetValues(CorrelationIdContextKeys.HttpHeaderName)
            .Should().ContainSingle().Which.Should().Be(explicitValue);
    }

    [Fact]
    public async Task SendAsync_WithoutAmbientContextOrActivity_DoesNotAddHeader()
    {
        // Arrange
        var accessor = new HttpContextAccessor { HttpContext = null };
        var capturing = new CapturingHandler();
        using var client = BuildClient(accessor, capturing);

        // Act (run outside any Activity)
        Activity.Current = null;
        _ = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest!.Headers.Contains(CorrelationIdContextKeys.HttpHeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WithActivityTagOnlyFallback_AddsHeaderFromActivity()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        var accessor = new HttpContextAccessor { HttpContext = null };
        using var source = new ActivitySource("Platform.ServiceDefaults.UnitTests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("background")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, correlationId);

        var capturing = new CapturingHandler();
        using var client = BuildClient(accessor, capturing);

        // Act
        _ = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest!.Headers.GetValues(CorrelationIdContextKeys.HttpHeaderName)
            .Should().ContainSingle().Which.Should().Be(correlationId);
    }

    private static HttpClient BuildClient(IHttpContextAccessor accessor, HttpMessageHandler terminal)
    {
        var handler = new CorrelationIdDelegatingHandler(accessor, DefaultOptions)
        {
            InnerHandler = terminal
        };
        return new HttpClient(handler, disposeHandler: true);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
