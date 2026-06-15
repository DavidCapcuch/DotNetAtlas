using FastEndpoints;

namespace EShop.BFF.Api.Endpoints;

/// <summary>FastEndpoints group rooting every BFF route under <c>/api/v1/bff/...</c> (ADR-0012).</summary>
internal sealed class BffGroup : Group
{
    public BffGroup()
    {
        Configure("/bff", ep =>
        {
            ep.Description(builder => builder.WithGroupName("BFF"));
            ep.Tags("BFF");
        });
    }
}
