using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Invoicing.ArchitectureTests.BoundedContext;

/// <summary>
/// Pins the <c>CreditNote</c> aggregate so it only references the <c>Invoice</c> aggregate
/// by Id (Guid), never by type. The <c>InvoiceSnapshot</c> VO is the sanctioned data carrier
/// from Invoice into CreditNote; CreditNote consumes the snapshot and stores the
/// <c>OriginalInvoiceId</c> + <c>OriginalInvoiceNumber</c>, never the Invoice aggregate itself.
/// </summary>
public class CreditNoteTests : BaseTest
{
    [Fact]
    public void CreditNote_ShouldNot_ReferenceInvoiceType_OnFieldsPropertiesOrParameters()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .And()
            .HaveName("CreditNote")
            .Should()
            .MeetCustomRule(new OnlyReferencesByIdRule(
                typeof(global::Invoicing.Domain.Invoices.Invoice)))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "CreditNote should reference Invoice only by Id (OriginalInvoiceId) and value-object " +
            "carriers (InvoiceSnapshot), never by holding an Invoice field/property/parameter " +
            "(DDD aggregate boundary; preserved by the snapshot refactor).");
    }
}
