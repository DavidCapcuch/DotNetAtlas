using FastEndpoints;

namespace Payments.Api.Endpoints.Payments;

/// <summary>
/// FastEndpoints group for the <c>/api/v1/payments/...</c> route family
/// (per ADR-0012 versioned-route convention; payments BC chapter §
/// admin-endpoints). Authentication is required by default; individual
/// endpoints opt into <c>AuthPolicies.PaymentsAdmin</c>.
/// </summary>
internal sealed class PaymentsGroup : Group
{
    public PaymentsGroup()
    {
        // Group route "payments" -> combined with FastEndpoints'
        // Endpoints.RoutePrefix="api" and Versioning.Prefix="v", endpoints
        // resolve to /api/v1/payments/... per the BC contract.
        Configure("payments", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Payments));
            ep.Tags(EndpointGroupConstants.Payments);
        });
    }
}
