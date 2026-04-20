using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Platform.ServiceDefaults.Resilience;

namespace Platform.ServiceDefaults.UnitTests.Resilience;

public class ResiliencePresetsTests
{
    [Fact]
    public async Task ReadIdempotent_Retries3TimesOnTransientFailure()
    {
        // Arrange — 3 retries = 4 total attempts (original + 3)
        var counter = new CallCounter();
        using var factory = BuildFactory(
            "reads",
            b => b.AddReadIdempotentResiliencePreset(ShrinkTimingsToMilliseconds),
            counter);
        using var client = factory.CreateClient("reads");

        // Act
        using var response = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            counter.Count.Should().Be(4);
        }
    }

    [Fact]
    public async Task WriteCommand_Retries1TimeOnTransientFailure()
    {
        // Arrange — 1 retry = 2 total attempts
        var counter = new CallCounter();
        using var factory = BuildFactory(
            "writes",
            b => b.AddWriteCommandResiliencePreset(ShrinkTimingsToMilliseconds),
            counter);
        using var client = factory.CreateClient("writes");

        // Act
        using var response = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            counter.Count.Should().Be(2);
        }
    }

    [Fact]
    public async Task BatchRead_Retries1TimeOnTransientFailure()
    {
        // Arrange — 1 retry = 2 total attempts
        var counter = new CallCounter();
        using var factory = BuildFactory(
            "batch",
            b => b.AddBatchReadResiliencePreset(ShrinkTimingsToMilliseconds),
            counter);
        using var client = factory.CreateClient("batch");

        // Act
        using var response = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            counter.Count.Should().Be(2);
        }
    }

    [Fact]
    public async Task Presets_AcceptCallerOverride()
    {
        // Arrange — caller overrides to a smaller retry budget (1 retry) on the read-idempotent preset.
        var counter = new CallCounter();
        using var factory = BuildFactory(
            "reads",
            b => b.AddReadIdempotentResiliencePreset(options =>
            {
                ShrinkTimingsToMilliseconds(options);
                options.Retry.MaxRetryAttempts = 1;
            }),
            counter);
        using var client = factory.CreateClient("reads");

        // Act
        using var response = await client.GetAsync(new Uri("http://localhost/ping"), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            counter.Count.Should().Be(2);
        }
    }

    private static void ShrinkTimingsToMilliseconds(HttpStandardResilienceOptions options)
    {
        // Collapse all durations so retry tests don't block CI.
        options.Retry.Delay = TimeSpan.FromMilliseconds(1);
        options.Retry.UseJitter = false;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    }

    private static ServiceProvider BuildFactory(
        string clientName,
        Action<IHttpClientBuilder> configurePipeline,
        HttpMessageHandler terminal)
    {
        var services = new ServiceCollection();
        var builder = services.AddHttpClient(clientName)
            .ConfigurePrimaryHttpMessageHandler(() => terminal);
        configurePipeline(builder);
        return services.BuildServiceProvider();
    }

    private sealed class CallCounter : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}

file static class ServiceProviderAsFactoryExtensions
{
    public static HttpClient CreateClient(this ServiceProvider provider, string name)
        => provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
}
