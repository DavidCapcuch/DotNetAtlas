using System.Net;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Platform.Api.UnitTests.Swagger.Probe;

/// <summary>
/// Puts <see cref="ContractProbeResponse"/> into the OpenAPI document. Declares <c>Version(1)</c>
/// because the platform document caps at <c>MaxEndpointVersion = 1</c> and would otherwise emit no
/// paths at all. Never invoked — only generated from.
/// </summary>
internal sealed class ContractProbeEndpoint : EndpointWithoutRequest<ContractProbeResponse>
{
    public override void Configure()
    {
        Get("/contract-probe");
        Version(1);
        AllowAnonymous();
        Description(b => b.Produces<ContractProbeResponse>((int)HttpStatusCode.OK));
    }

    public override Task HandleAsync(CancellationToken ct)
        => Send.OkAsync(
            new ContractProbeResponse { ProductId = Guid.Empty, Sku = string.Empty, Note = null },
            ct);
}
