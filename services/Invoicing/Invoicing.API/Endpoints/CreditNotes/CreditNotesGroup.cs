using FastEndpoints;

namespace Invoicing.API.Endpoints.CreditNotes;

/// <summary>
/// FastEndpoints group for the <c>/api/v1/invoicing/credit-notes/...</c> route family
/// (per ADR-0012).
/// </summary>
internal sealed class CreditNotesGroup : Group
{
    public CreditNotesGroup()
    {
        Configure("invoicing/credit-notes", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName(EndpointGroupConstants.CreditNotes));
            ep.Tags(EndpointGroupConstants.CreditNotes);
        });
    }
}
