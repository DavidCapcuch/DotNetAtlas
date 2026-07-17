using Payments.Domain.Transactions.ValueObjects;

namespace Payments.UnitTests.Transactions.ValueObjects;

public class PaymentStatusTests
{
    public static TheoryData<PaymentStatus, PaymentStatus, bool> TransitionMatrix
    {
        get
        {
            // Allowed transitions per plan § PaymentStatus transition table:
            //   Requested  -> Authorized, Failed
            //   Authorized -> Captured, Failed, Voided
            //   Captured   -> Completed, Refunded
            //   Completed  -> Refunded
            //   Failed, Voided, Refunded -> (none)
            var allowed = new HashSet<(PaymentStatus From, PaymentStatus To)>
            {
                (PaymentStatus.Requested, PaymentStatus.Authorized),
                (PaymentStatus.Requested, PaymentStatus.Failed),
                (PaymentStatus.Authorized, PaymentStatus.Captured),
                (PaymentStatus.Authorized, PaymentStatus.Failed),
                (PaymentStatus.Authorized, PaymentStatus.Voided),
                (PaymentStatus.Captured, PaymentStatus.Completed),
                (PaymentStatus.Captured, PaymentStatus.Refunded),
                (PaymentStatus.Completed, PaymentStatus.Refunded),
            };

            // The full From×To grid — every self-transition (From == To) is present and expected
            // false, so a dedicated "self-transitions rejected" test would be redundant.
            var data = new TheoryData<PaymentStatus, PaymentStatus, bool>();
            foreach (var from in PaymentStatus.List)
            {
                foreach (var to in PaymentStatus.List)
                {
                    data.Add(from, to, allowed.Contains((from, to)));
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void CanTransitionTo_MatchesMatrix(PaymentStatus from, PaymentStatus to, bool expected)
    {
        // Arrange & Act
        var canTransition = from.CanTransitionTo(to);

        // Assert
        canTransition.Should().Be(expected);
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Failed))]
    [InlineData(nameof(PaymentStatus.Voided))]
    [InlineData(nameof(PaymentStatus.Refunded))]
    public void IsFinal_ReturnsTrue_ForCompensationTerminals(string statusName)
    {
        // Arrange
        var status = PaymentStatus.FromName(statusName);

        // Act & Assert
        status.IsFinal.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(PaymentStatus.Requested))]
    [InlineData(nameof(PaymentStatus.Authorized))]
    [InlineData(nameof(PaymentStatus.Captured))]
    [InlineData(nameof(PaymentStatus.Completed))]
    public void IsFinal_ReturnsFalse_ForNonFinalStates(string statusName)
    {
        // Arrange
        var status = PaymentStatus.FromName(statusName);

        // Act & Assert
        status.IsFinal.Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_WhenTargetNull_Throws()
    {
        // Arrange
        var action = () => PaymentStatus.Requested.CanTransitionTo(null!);

        // Act & Assert
        action.Should().Throw<ArgumentNullException>();
    }
}
