using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Errors;
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

        using (new AssertionScope())
        {
            line.LineTotal.Amount.Should().Be(300m);
            line.LineTotal.Currency.Name.Should().Be("EUR");
        }
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

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-50)]
    public void Create_RejectsNonPositiveUnitPrice(decimal nonPositive)
    {
        // Local Invoicing-domain invariant: InvoiceLine.UnitPrice > 0. Money is a signed
        // quantity (School B); sign-enforcement belongs to the aggregate / VO.
        var result = InvoiceLine.Create(
            1,
            Sku.Create("S").Value,
            "desc",
            quantity: 1,
            Money.Create(nonPositive, "EUR").Value,
            VatRate.Create(0m).Value);

        result.IsSuccess.Should().BeFalse();
        var error = result.Errors[0] as ValidationError;
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("Invoicing.InvoiceLineUnitPriceMustBePositive");
    }
}
