using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.CorrelationId;

namespace Platform.ServiceDefaults.UnitTests.CorrelationId;

public class CorrelationIdServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCorrelationId_RegistersDelegatingHandlerAndOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // Act
        services.AddCorrelationId();

        // Assert
        using var provider = services.BuildServiceProvider();
        using (new AssertionScope())
        {
            provider.GetService<IHttpContextAccessor>().Should().NotBeNull();
            provider.GetService<CorrelationIdDelegatingHandler>().Should().NotBeNull();
            provider.GetRequiredService<IOptions<CorrelationIdOptions>>().Value.HeaderName
                .Should().Be(CorrelationIdContextKeys.HttpHeaderName);
        }
    }

    [Fact]
    public async Task AddCorrelationIdPropagation_WritesAmbientCorrelationIdOnOutboundRequest()
    {
        // Arrange — full round-trip through the IHttpClientFactory pipeline confirms the delegating
        // handler is actually attached to the named client (not just resolvable from DI).
        var correlationId = Guid.CreateVersion7().ToString();
        var capturing = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCorrelationId();
        services.AddHttpClient("downstream")
            .ConfigurePrimaryHttpMessageHandler(() => capturing)
            .AddCorrelationIdPropagation();

        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Items[CorrelationIdContextKeys.HttpContextItemKey] = correlationId;

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
