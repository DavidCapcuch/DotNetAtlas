using System.Diagnostics;
using Platform.ServiceDefaults.CorrelationId;

namespace Platform.ServiceDefaults.UnitTests.CorrelationId;

public class CorrelationIdDelegatingHandlerTests
{
    private static readonly ActivitySource Source = new("Platform.ServiceDefaults.UnitTests");

    public CorrelationIdDelegatingHandlerTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public async Task SendAsync_WithAmbientActivityTag_AddsHeaderToOutbound()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7().ToString();
        using var activity = Source.StartActivity("test")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, correlationId);

        var capturing = new CapturingHandler();
        using var client = BuildClient(capturing);

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
        using var activity = Source.StartActivity("test")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, ambientValue);

        var capturing = new CapturingHandler();
        using var client = BuildClient(capturing);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping");
        request.Headers.Add(CorrelationIdContextKeys.HttpHeaderName, explicitValue);

        // Act
        _ = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest!.Headers.GetValues(CorrelationIdContextKeys.HttpHeaderName)
            .Should().ContainSingle().Which.Should().Be(explicitValue);
    }

    [Fact]
    public async Task SendAsync_WithoutAmbientActivity_DoesNotAddHeader()
    {
        // Arrange
        var capturing = new CapturingHandler();
        using var client = BuildClient(capturing);

        // Act (run outside any Activity)
        Activity.Current = null;
        _ = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest!.Headers.Contains(CorrelationIdContextKeys.HttpHeaderName).Should().BeFalse();
    }

    private static HttpClient BuildClient(HttpMessageHandler terminal)
    {
        var handler = new CorrelationIdDelegatingHandler
        {
            InnerHandler = terminal,
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
