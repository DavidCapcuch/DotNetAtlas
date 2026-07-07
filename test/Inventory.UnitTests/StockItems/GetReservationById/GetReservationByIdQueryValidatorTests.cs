using FluentValidation.TestHelper;
using Inventory.Application.StockItems.GetReservationById;

namespace Inventory.UnitTests.StockItems.GetReservationById;

public sealed class GetReservationByIdQueryValidatorTests
{
    private readonly GetReservationByIdQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyReservationId_Fails()
    {
        var result = _validator.TestValidate(new GetReservationByIdQuery { ReservationId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(q => q.ReservationId);
    }

    [Fact]
    public void Validate_NonEmptyReservationId_Passes()
    {
        var result = _validator.TestValidate(new GetReservationByIdQuery { ReservationId = Guid.CreateVersion7() });

        result.ShouldNotHaveValidationErrorFor(q => q.ReservationId);
    }
}
