using Invoicing.Domain.Invoices.ValueObjects;

namespace Invoicing.UnitTests.Invoices.ValueObjects;

public class InvoiceStatusTests
{
    public static TheoryData<InvoiceStatus, InvoiceStatus> ValidTransitions => new()
    {
        { InvoiceStatus.Draft, InvoiceStatus.Issued },
        { InvoiceStatus.Draft, InvoiceStatus.Cancelled },
        { InvoiceStatus.Issued, InvoiceStatus.Delivered },
        { InvoiceStatus.Issued, InvoiceStatus.Cancelled },
        { InvoiceStatus.Delivered, InvoiceStatus.Archived },
        { InvoiceStatus.Delivered, InvoiceStatus.Cancelled },
    };

    public static TheoryData<InvoiceStatus, InvoiceStatus> InvalidTransitions => new()
    {
        { InvoiceStatus.Draft, InvoiceStatus.Delivered },   // skip Issued
        { InvoiceStatus.Draft, InvoiceStatus.Archived },    // skip Issued + Delivered
        { InvoiceStatus.Issued, InvoiceStatus.Archived },   // skip Delivered
        { InvoiceStatus.Archived, InvoiceStatus.Issued },   // terminal
        { InvoiceStatus.Archived, InvoiceStatus.Cancelled }, // terminal
        { InvoiceStatus.Cancelled, InvoiceStatus.Issued },  // terminal
        { InvoiceStatus.Cancelled, InvoiceStatus.Delivered }, // terminal
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransitionTo_AllowedPairs_Succeeds(InvoiceStatus from, InvoiceStatus to)
    {
        from.CanTransitionTo(to).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void CanTransitionTo_DisallowedPairs_Fails(InvoiceStatus from, InvoiceStatus to)
    {
        from.CanTransitionTo(to).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_Null_Throws()
    {
        var act = () => InvoiceStatus.Draft.CanTransitionTo(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
