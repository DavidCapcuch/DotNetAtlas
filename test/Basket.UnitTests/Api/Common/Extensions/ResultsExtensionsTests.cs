using Basket.Api.Common.Extensions;
using Basket.Domain.Baskets.Errors;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Api.Common.Extensions;

/// <summary>
/// Pure-mapping coverage for <see cref="ResultsExtensions.ResolveErrorResponse"/> — the seam
/// that translates a failed <see cref="ResultBase"/> into the (validation failures, status)
/// pair sent back through <c>SendErrorsAsync</c>. Keeps the I/O side of the extension method
/// out of unit-test scope; the live <c>SendErrorResponseAsync</c> is exercised end-to-end by
/// the functional-test slice.
/// </summary>
public class ResultsExtensionsTests
{
    [Fact]
    public void ResolveErrorResponse_WhenBasketConcurrencyError_Returns409WithBasketConcurrencyCode()
    {
        // basket.md § 5.4 + error-taxonomy.md § 3.1 contract: a CAS retry-exhaustion
        // surfaces as 409, NOT as the generic 500 "An unexpected error occurred" path.
        // Before the fix the switch in ResolveErrorResponse matched none of
        // BasketConcurrencyError's interfaces and fell through to the no-domain-error
        // branch — every concurrency loss became a 5xx on the client and an APM alert.
        var error = new BasketConcurrencyError(Guid.CreateVersion7(), Expected: 3, Actual: 5);
        var result = Result.Fail(error);

        var (failures, statusCode) = ResultsExtensions.ResolveErrorResponse(result);

        using (new AssertionScope())
        {
            statusCode.Should().Be(StatusCodes.Status409Conflict);
            failures.Should()
                .ContainSingle()
                .Which.ErrorCode.Should().Be("Basket.Concurrency");
        }
    }

    [Fact]
    public void ResolveErrorResponse_WhenValidationErrorBasketEmpty_Returns409FromBasketStatusMap()
    {
        var result = Result.Fail(BasketErrors.EmptyBasket());

        var (failures, statusCode) = ResultsExtensions.ResolveErrorResponse(result);

        using (new AssertionScope())
        {
            statusCode.Should().Be(StatusCodes.Status409Conflict);
            failures.Should()
                .ContainSingle()
                .Which.ErrorCode.Should().Be("Basket.Empty");
        }
    }

    [Fact]
    public void ResolveErrorResponse_WhenNotFoundError_Returns404()
    {
        var result = Result.Fail(new NotFoundError("Basket", Guid.CreateVersion7(), "Basket.NotFound"));

        var (failures, statusCode) = ResultsExtensions.ResolveErrorResponse(result);

        using (new AssertionScope())
        {
            statusCode.Should().Be(StatusCodes.Status404NotFound);
            failures.Should().ContainSingle();
        }
    }
}
