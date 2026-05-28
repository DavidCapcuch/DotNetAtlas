using FluentResults;
using Microsoft.AspNetCore.Http;
using Platform.Api.Extensions;
using Platform.SharedKernel.Errors;

namespace Platform.Api.UnitTests;

/// <summary>
/// Unit tests for the pure mapping logic in
/// <see cref="ResponseSenderExtensions.MapToProblem"/>. The public extension method
/// is a one-liner over <c>MapToProblem</c> + <c>SendErrorsAsync</c>, so all dispatch
/// behaviour is exercised here without an <see cref="HttpContext"/> dependency.
/// </summary>
public class ResponseSenderExtensionsTests
{
    [Fact]
    public void ValidationError_maps_to_422()
    {
        var result = Result.Fail(new ValidationError("Name", "must not be empty", "Foo.NameRequired"));

        var (failures, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        failures.Should().HaveCount(1);
        failures[0].PropertyName.Should().Be("Name");
        failures[0].ErrorCode.Should().Be("Foo.NameRequired");
    }

    [Fact]
    public void NotFoundError_maps_to_404()
    {
        var result = Result.Fail(new NotFoundError("Product", Guid.Empty, "Product.NotFound"));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ConflictError_maps_to_409()
    {
        var result = Result.Fail(new ConflictError("Product", "SKU already exists", "Product.SkuAlreadyExists"));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ForbiddenError_maps_to_403()
    {
        var result = Result.Fail(new ForbiddenError("Product", Guid.Empty, "Product.AdminOnly"));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void NotImplementedError_maps_to_501()
    {
        var result = Result.Fail(new NotImplementedError(
            "PartialRefund",
            "Partial refunds are not supported in v1",
            "Invoicing.PartialRefundNotSupportedV1"));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status501NotImplemented);
    }

    [Fact]
    public void ServiceUnavailableError_maps_to_503()
    {
        var result = Result.Fail(new ServiceUnavailableError(
            "Catalog",
            "catalog API timed out",
            "Basket.CatalogUnavailable"));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void Unknown_DomainError_subclass_maps_to_400()
    {
        var result = Result.Fail(new UnknownDomainError());

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Non_DomainError_IError_maps_to_500_with_synthetic_failure()
    {
        var result = Result.Fail(new Error("bare FluentResults error"));

        var (failures, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        failures.Should().HaveCount(1);
        failures[0].PropertyName.Should().Be("internal_error");
    }

    [Fact]
    public void Multiple_errors_with_different_statuses_pick_most_severe_503()
    {
        var result = Result.Fail(new IError[]
        {
            new ValidationError("Name", "x", "Foo.A"),
            new NotFoundError("Bar", Guid.Empty, "Foo.B"),
            new ConflictError("Baz", "x", "Foo.C"),
            new ServiceUnavailableError("Upstream", "down", "Foo.D"),
        });

        var (failures, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        failures.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(new[] { "Validation", "NotFound" }, StatusCodes.Status404NotFound)]
    [InlineData(new[] { "NotFound", "Conflict" }, StatusCodes.Status409Conflict)]
    [InlineData(new[] { "Conflict", "Forbidden" }, StatusCodes.Status403Forbidden)]
    [InlineData(new[] { "Forbidden", "NotImplemented" }, StatusCodes.Status501NotImplemented)]
    [InlineData(new[] { "NotImplemented", "ServiceUnavailable" }, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(new[] { "Validation", "ServiceUnavailable" }, StatusCodes.Status503ServiceUnavailable)]
    public void Precedence_table(string[] errorKinds, int expectedStatus)
    {
        var result = Result.Fail(errorKinds.Select(BuildError));

        var (_, statusCode) = ResponseSenderExtensions.MapToProblem(result);

        statusCode.Should().Be(expectedStatus);
    }

    private static IError BuildError(string kind) => kind switch
    {
        "Validation" => new ValidationError("X", "x", "x"),
        "NotFound" => new NotFoundError("X", Guid.Empty, "x"),
        "Conflict" => new ConflictError("X", "x", "x"),
        "Forbidden" => new ForbiddenError("X", Guid.Empty, "x"),
        "NotImplemented" => new NotImplementedError("X", "x", "x"),
        "ServiceUnavailable" => new ServiceUnavailableError("X", "x", "x"),
        _ => throw new ArgumentException($"Unknown kind: {kind}", nameof(kind)),
    };

    private sealed class UnknownDomainError() : DomainError("a future error type not yet mapped", "Unknown.Code");
}
