using NetArchTest.Rules;
using Platform.SharedKernel.Base;

namespace Invoicing.ArchitectureTests.BoundedContext;

/// <summary>
/// Per architecture-tests.md § 2.1, pins the <c>Invoice</c> aggregate so it only references
/// the <c>CreditNote</c> aggregate by Id (Guid), never by type, on fields, properties, or
/// method parameters. Catalog's reference test pins the same shape for
/// <c>Product</c> ↛ <c>Category</c>.
/// </summary>
public class InvoiceTests : BaseTest
{
    [Fact]
    public void Invoice_ShouldNot_ReferenceCreditNoteType_OnFieldsPropertiesOrParameters()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(AggregateRoot<>))
            .And()
            .HaveName("Invoice")
            .Should()
            .MeetCustomRule(new OnlyReferencesByIdRule(
                typeof(global::Invoicing.Domain.CreditNotes.CreditNote)))
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "Invoice should reference CreditNote only by Id (CreditNoteId on CancellationInfo), " +
            "never by holding a CreditNote field/property/parameter (DDD aggregate boundary).");
    }
}
