using Invoicing.Domain.CreditNotes.ValueObjects;

namespace Invoicing.UnitTests.CreditNotes.ValueObjects;

public class CreditNoteNumberTests
{
    [Fact]
    public void Create_WithValidInputs_FormatsCanonicalString()
    {
        CreditNoteNumber.Create(2026, 8).Value.Value.Should().Be("CN-2026-000008");
    }

    [Theory]
    [InlineData("CN-2026-000008")]
    [InlineData("CN-1999-999999")]
    public void FromRaw_AcceptsCanonicalFormat(string raw)
    {
        CreditNoteNumber.FromRaw(raw).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("INV-2026-000008")]
    [InlineData("cn-2026-000008")]
    [InlineData("CN-2026-8")]
    public void FromRaw_RejectsInvalidFormat(string raw)
    {
        CreditNoteNumber.FromRaw(raw).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(10000)]
    public void Create_RejectsYearOutsideRange(int year)
    {
        CreditNoteNumber.Create(year, 1).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void Create_RejectsSequenceOutsideRange(long sequence)
    {
        CreditNoteNumber.Create(2026, sequence).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void YearAndSequence_DerivedFromValue()
    {
        var number = CreditNoteNumber.Create(2027, 42).Value;

        number.Year.Should().Be(2027);
        number.Sequence.Should().Be(42);
    }
}
