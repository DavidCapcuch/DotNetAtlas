using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.CorrelationId;

namespace Platform.ServiceDefaults.UnitTests.CorrelationId;

public class CorrelationIdServiceCollectionExtensionsTests
{
    private static readonly ActivitySource Source = new("Platform.ServiceDefaults.UnitTests");

    public CorrelationIdServiceCollectionExtensionsTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public void AddCorrelationId_RegistersDelegatingHandler()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCorrelationId();

        // Assert
        using var provider = services.BuildServiceProvider();
        provider.GetService<CorrelationIdDelegatingHandler>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddCorrelationIdPropagation_WritesAmbientCorrelationIdOnOutboundRequest()
    {
        // Arrange — full round-trip through the IHttpClientFactory pipeline confirms the delegating
        // handler is actually attached to the named client (not just resolvable from DI).
        var correlationId = Guid.CreateVersion7().ToString();
        var capturing = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddCorrelationId();
        services.AddHttpClient("downstream")
            .ConfigurePrimaryHttpMessageHandler(() => capturing)
            .AddCorrelationIdPropagation();

        using var provider = services.BuildServiceProvider();
        using var activity = Source.StartActivity("test")!;
        activity.SetTag(CorrelationIdContextKeys.ActivityTagName, correlationId);

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("downstream");

        // Act
        _ = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        capturing.LastRequest.Should().NotBeNull();
        capturing.LastRequest!.Headers.GetValues(CorrelationIdContextKeys.HttpHeaderName)
            .Should().ContainSingle().Which.Should().Be(correlationId);
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
