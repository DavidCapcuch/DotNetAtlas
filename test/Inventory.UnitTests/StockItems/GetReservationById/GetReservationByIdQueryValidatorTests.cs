using FluentValidation.TestHelper;
using Inventory.Application.StockItems.GetReservationById;

namespace Inventory.UnitTests.StockItems.GetReservationById;

public sealed class GetReservationByIdQueryValidatorTests
{
    private readonly GetReservationByIdQueryValidator _validator = new();

    [Fact]
    public void EmptyReservationId_FailsValidation()
    {
        var result = _validator.TestValidate(new GetReservationByIdQuery { ReservationId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(q => q.ReservationId);
    }

    [Fact]
    public void NonEmptyReservationId_PassesValidation()
    {
        var result = _validator.TestValidate(new GetReservationByIdQuery { ReservationId = Guid.CreateVersion7() });

        result.ShouldNotHaveValidationErrorFor(q => q.ReservationId);
    }
}
