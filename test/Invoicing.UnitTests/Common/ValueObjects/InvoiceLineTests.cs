using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class InvoiceLineTests
{
    [Fact]
    public void Create_ComputesLineTotal()
    {
        var line = InvoiceLine.Create(
            lineNumber: 1,
            sku: Sku.Create("WIDGET-001").Value,
            description: "Widget",
            quantity: 3,
            unitPrice: Money.Create(100m, "EUR").Value,
            vatRate: VatRate.Create(21m).Value).Value;

        line.LineTotal.Amount.Should().Be(300m);
        line.LineTotal.Currency.Name.Should().Be("EUR");
    }

    [Fact]
    public void Create_RejectsZeroQuantity()
    {
        var result = InvoiceLine.Create(
            1,
            Sku.Create("S").Value,
            "desc",
            quantity: 0,
            Money.Create(10m, "EUR").Value,
            VatRate.Create(0m).Value);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Create_RejectsEmptyDescription()
    {
        InvoiceLine.Create(1, Sku.Create("S").Value, "", 1, Money.Create(10m, "EUR").Value, VatRate.Create(0m).Value)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void WithFlippedSign_NegatesUnitPriceAndLineTotal()
    {
        var line = InvoiceLine.Create(
            1,
            Sku.Create("S").Value,
            "desc",
            quantity: 2,
            Money.Create(50m, "EUR").Value,
            VatRate.Create(21m).Value).Value;

        var flipped = line.WithFlippedSign();

        flipped.UnitPrice.Amount.Should().Be(-50m);
        flipped.LineTotal.Amount.Should().Be(-100m);
        flipped.Quantity.Should().Be(2);
        flipped.Sku.Should().Be(line.Sku);
    }
}
