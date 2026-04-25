using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Errors;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Catalog.Application.Products.DescribeProduct;

public sealed class DescribeProductCommandHandler : ICommandHandler<DescribeProductCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DescribeProductCommandHandler> _logger;

    public DescribeProductCommandHandler(
        ICatalogDbContext db,
        TimeProvider timeProvider,
        ILogger<DescribeProductCommandHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(DescribeProductCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        var descriptionResult = ProductDescription.Create(command.NewDescription);
        if (descriptionResult.IsFailed)
        {
            return descriptionResult.ToResult();
        }

        var describeResult = product.Describe(descriptionResult.Value, _timeProvider.GetUtcNow());
        if (describeResult.IsFailed)
        {
            return describeResult;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated description for Product {ProductId}", product.Id);

        return Result.Ok();
    }
}
