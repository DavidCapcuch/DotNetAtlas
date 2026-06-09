using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// Mailpit dev SMTP sink as a Testcontainer (ADR-0032 § 6). Exposes the SMTP endpoint the email
/// dispatcher sends to and the REST API tests assert the captured mail against.
/// </summary>
public sealed class MailpitTestContainer : IAsyncDisposable
{
    private const ushort SmtpContainerPort = 1025;
    private const ushort ApiContainerPort = 8025;

    private readonly IContainer _container;
    private HttpClient _apiClient = null!;

    public string ImageName => "axllent/mailpit:v1.21";

    public string SmtpHost { get; private set; } = null!;

    public int SmtpPort { get; private set; }

    public MailpitTestContainer()
    {
        _container = new ContainerBuilder(ImageName)
            .WithName($"TestMailpitFixture-{Guid.NewGuid()}")
            .WithPortBinding(SmtpContainerPort, true)
            .WithPortBinding(ApiContainerPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(ApiContainerPort).ForPath("/readyz")))
            .WithCleanUp(true)
            .Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _container.StartAsync(cancellationToken);
        SmtpHost = _container.Hostname;
        SmtpPort = _container.GetMappedPublicPort(SmtpContainerPort);
        _apiClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiContainerPort)}"),
        };
    }

    /// <summary>Returns every message Mailpit has captured (newest first).</summary>
    public async Task<IReadOnlyList<MailpitMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.GetFromJsonAsync<MailpitMessagesResponse>(
            "/api/v1/messages", cancellationToken);
        return response?.Messages ?? [];
    }

    /// <summary>Fetches one captured message in full (including the rendered text body) by its Mailpit ID.</summary>
    public async Task<MailpitMessageDetail> GetMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        var detail = await _apiClient.GetFromJsonAsync<MailpitMessageDetail>(
            $"/api/v1/message/{id}", cancellationToken);
        return detail ?? throw new InvalidOperationException($"Mailpit message '{id}' was not found.");
    }

    /// <summary>Deletes all captured messages (call between tests).</summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/messages");
        using var response = await _apiClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _apiClient?.Dispose();
        await _container.DisposeAsync();
    }
}

public sealed record MailpitMessagesResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("messages")] IReadOnlyList<MailpitMessage> Messages);

public sealed record MailpitMessage(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("To")] IReadOnlyList<MailpitContact> To);

public sealed record MailpitContact(
    [property: JsonPropertyName("Address")] string Address);

public sealed record MailpitMessageDetail(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("Text")] string Text);
