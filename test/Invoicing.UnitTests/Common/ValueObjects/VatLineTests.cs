using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class VatLineTests
{
    [Fact]
    public void Create_PositiveBaseAndAmount_Succeeds()
    {
        var rate = VatRate.Create(21m).Value;
        var @base = Money.Create(200m, CurrencyCode.Eur).Value;
        var amount = Money.Create(42m, CurrencyCode.Eur).Value;

        var line = VatLine.Create(rate, @base, amount);

        using (new AssertionScope())
        {
            line.Rate.Should().Be(rate);
            line.Base.Should().Be(@base);
            line.Amount.Should().Be(amount);
        }
    }

    [Fact]
    public void Create_ZeroBaseAndZeroAmount_Succeeds()
    {
        // Zero-rate VAT lines are legal (v1 ships every line at 0% per IssueInvoiceCommandHandler).
        var rate = VatRate.Create(0m).Value;
        var @base = Money.Create(0m, CurrencyCode.Eur).Value;
        var amount = Money.Create(0m, CurrencyCode.Eur).Value;

        var line = VatLine.Create(rate, @base, amount);

        line.Base.Amount.Should().Be(0m);
        line.Amount.Amount.Should().Be(0m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_NegativeBase_ThrowsDataIntegrityException(decimal negative)
    {
        // Local Invoicing-domain invariant: VatLine.Base >= 0. Money itself is sign-neutral;
        // sign-enforcement belongs to the consuming VO.
        var act = () => VatLine.Create(
            VatRate.Create(21m).Value,
            Money.Create(negative, CurrencyCode.Eur).Value,
            Money.Create(0m, CurrencyCode.Eur).Value);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.VatLineBaseNegative");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-42)]
    public void Create_NegativeAmount_ThrowsDataIntegrityException(decimal negative)
    {
        var act = () => VatLine.Create(
            VatRate.Create(21m).Value,
            Money.Create(200m, CurrencyCode.Eur).Value,
            Money.Create(negative, CurrencyCode.Eur).Value);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Invoicing.VatLineAmountNegative");
    }
}
