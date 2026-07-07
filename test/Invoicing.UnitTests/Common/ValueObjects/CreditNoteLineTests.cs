using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class CreditNoteLineTests
{
    [Fact]
    public void FromInvoiceLine_FlipsSignsOnAmountsAndPreservesEverythingElse()
    {
        // Arrange
        var invoiceLine = InvoiceLine.Create(
            lineNumber: 7,
            sku: Sku.Create("WIDGET-001").Value,
            description: "Widget",
            quantity: 2,
            unitPrice: Money.Create(50m, "EUR").Value,
            vatRate: VatRate.Create(21m).Value).Value;

        // Act
        var creditLine = CreditNoteLine.FromInvoiceLine(invoiceLine);

        // Assert
        using (new AssertionScope())
        {
            creditLine.UnitPrice.Amount.Should().Be(-50m);
            creditLine.LineTotal.Amount.Should().Be(-100m);
            creditLine.UnitPrice.Currency.Should().Be(invoiceLine.UnitPrice.Currency);
            creditLine.LineTotal.Currency.Should().Be(invoiceLine.LineTotal.Currency);
            creditLine.Quantity.Should().Be(invoiceLine.Quantity);
            creditLine.LineNumber.Should().Be(invoiceLine.LineNumber);
            creditLine.Sku.Should().Be(invoiceLine.Sku);
            creditLine.Description.Should().Be(invoiceLine.Description);
            creditLine.VatRate.Should().Be(invoiceLine.VatRate);
        }
    }

    [Fact]
    public void FromInvoiceLine_ThrowsOnNull()
    {
        var act = () => CreditNoteLine.FromInvoiceLine(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
