using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.UnitTests.Application.Common;
using Platform.SharedKernel.Exceptions;

namespace Ordering.UnitTests.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Handler-level defence-in-depth pin for #241. The FluentValidation
/// validator (<see cref="GetOrdersByBuyerQueryValidator"/>) is the
/// front-line guard for PageNumber / PageSize. This test bypasses the
/// validation pipeline by constructing the handler directly and asserts
/// that an out-of-range PageNumber / PageSize is still rejected with a
/// bug-class <see cref="DataIntegrityException"/> rather than degrading
/// silently (PageSize=0 → empty page; PageNumber=0 → negative EF offset).
/// </summary>
public class GetOrdersByBuyerQueryHandlerTests : HandlerTestBase
{
    private GetOrdersByBuyerQueryHandler CreateHandler() => new(DbContext);

    [Theory]
    [InlineData(0, 20)] // PageNumber=0 → (0-1)*20 = -20 offset, undefined EF behaviour
    [InlineData(-1, 20)] // PageNumber<0 → even more negative offset
    [InlineData(1, 0)] // PageSize=0 → Take(0) silent-empty-page bug class
    [InlineData(1, -5)] // PageSize<0 → Take(<0) undefined behaviour
    [InlineData(1, 101)] // PageSize above MaxPageSize=100 → unbounded query
    public async Task Handle_OutOfRangePageNumberOrPageSize_ThrowsDataIntegrityException(int pageNumber, int pageSize)
    {
        var query = new GetOrdersByBuyerQuery
        {
            BuyerId = Guid.CreateVersion7(),
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

        var act = () => CreateHandler().HandleAsync(query, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("OrdersByBuyer.OutOfRange");
    }
}
