using FastEndpoints;

namespace Invoicing.Api.Endpoints.Invoices;

/// <summary>
/// FastEndpoints group for the <c>/api/v1/invoicing/invoices/...</c> route family
/// (per ADR-0012). Authentication is the default; individual endpoints opt into
/// <c>AuthPolicies.InvoicingAdmin</c> for the admin-only routes.
/// </summary>
internal sealed class InvoicesGroup : Group
{
    public InvoicesGroup()
    {
        // Group route: "invoicing/invoices" -> combined with FastEndpoints'
        // Endpoints.RoutePrefix="api" and Versioning.Prefix="v", endpoints resolve to
        // /api/v1/invoicing/invoices/... (ADR-0012).
        Configure("invoicing/invoices", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.Invoices));
            ep.Tags(EndpointGroupConstants.Invoices);
        });
    }
}
