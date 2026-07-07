using Invoicing.Domain.Invoices.ValueObjects;

namespace Invoicing.UnitTests.Invoices.ValueObjects;

public class InvoiceNumberTests
{
    [Fact]
    public void Create_WithValidInputs_FormatsCanonicalString()
    {
        var result = InvoiceNumber.Create(2026, 142);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Value.Should().Be("INV-2026-000142");
        }
    }

    [Theory]
    [InlineData(2026, 1, "INV-2026-000001")]
    [InlineData(1999, 999999, "INV-1999-999999")]
    [InlineData(9999, 1, "INV-9999-000001")]
    public void Create_PadsSequenceToSixDigits(int year, long sequence, string expected)
    {
        InvoiceNumber.Create(year, sequence).Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(10000)]
    public void Create_RejectsYearOutsideRange(int year)
    {
        var result = InvoiceNumber.Create(year, 1);

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    public void Create_RejectsSequenceOutsideRange(long sequence)
    {
        var result = InvoiceNumber.Create(2026, sequence);

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("INV-2026-000142")]
    [InlineData("INV-1900-000001")]
    public void FromRaw_AcceptsCanonicalFormat(string raw)
    {
        InvoiceNumber.FromRaw(raw).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("inv-2026-000142")] // lowercase
    [InlineData("INV-2026-142")] // missing padding
    [InlineData("INV-2026-0000142")] // 7 digits
    [InlineData("CN-2026-000142")] // wrong prefix
    [InlineData("INV-26-000142")] // 2-digit year
    public void FromRaw_RejectsInvalidFormat(string raw)
    {
        InvoiceNumber.FromRaw(raw).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void YearAndSequence_DerivedFromValue()
    {
        var number = InvoiceNumber.Create(2027, 9876).Value;

        using (new AssertionScope())
        {
            number.Year.Should().Be(2027);
            number.Sequence.Should().Be(9876);
        }
    }
}
