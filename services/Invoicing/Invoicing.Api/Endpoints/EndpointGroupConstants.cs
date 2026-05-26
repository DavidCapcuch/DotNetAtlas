namespace Invoicing.Api.Endpoints;

/// <summary>
/// FastEndpoints group / tag names used across <c>Invoicing.Api</c>.
/// </summary>
/// <remarks>
/// <para>
/// These literals are the public OpenAPI tags AND the second URL segment of the
/// versioned group routes — <see cref="Invoices"/> appears as <c>/api/v1/invoicing/invoices</c>
/// (configured in <c>Invoices/InvoicesGroup.cs</c>) and as the Swagger tag the FE
/// SDK generator buckets endpoints by. Renaming the constant without updating the
/// matching <c>Group</c> route prefix (or vice versa) breaks tag continuity for any
/// downstream consumer that pins on the OpenAPI tag (closeout1 L3).
/// </para>
/// </remarks>
internal static class EndpointGroupConstants
{
    public const string Invoices = "invoices";

    public const string CreditNotes = "credit-notes";
}
