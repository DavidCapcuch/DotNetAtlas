using Invoicing.Domain.CreditNotes.ValueObjects;

namespace Invoicing.UnitTests.CreditNotes.ValueObjects;

public class CreditNoteStatusTests
{
    [Fact]
    public void Issued_CanTransitionToDelivered()
    {
        CreditNoteStatus.Issued.CanTransitionTo(CreditNoteStatus.Delivered).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Delivered_CanTransitionToArchived()
    {
        CreditNoteStatus.Delivered.CanTransitionTo(CreditNoteStatus.Archived).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidPairs))]
    public void CanTransitionTo_DisallowedPair_Fails(CreditNoteStatus from, CreditNoteStatus to)
    {
        from.CanTransitionTo(to).IsSuccess.Should().BeFalse();
    }

    public static TheoryData<CreditNoteStatus, CreditNoteStatus> InvalidPairs => new()
    {
        { CreditNoteStatus.Issued, CreditNoteStatus.Archived },  // skip Delivered
        { CreditNoteStatus.Delivered, CreditNoteStatus.Issued },  // regress
        { CreditNoteStatus.Archived, CreditNoteStatus.Issued },   // terminal
        { CreditNoteStatus.Archived, CreditNoteStatus.Delivered }, // terminal
    };
}
