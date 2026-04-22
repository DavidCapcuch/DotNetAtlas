using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Application.Products.UpdateProductPrice;

/// <summary>
/// Handler for <see cref="UpdateProductPriceCommand"/>. Loads the product, calls
/// <see cref="Catalog.Domain.Products.Product.UpdatePrice"/>, and persists. The domain method
/// is a no-op when the new price equals the current one; discontinued products reject the change.
/// </summary>
public sealed class UpdateProductPriceCommandHandler : ICommandHandler<UpdateProductPriceCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<UpdateProductPriceCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public UpdateProductPriceCommandHandler(
        ICatalogDbContext db,
        ILogger<UpdateProductPriceCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(UpdateProductPriceCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        var priceResult = Money.Create(command.NewPrice.Amount, command.NewPrice.Currency);
        if (priceResult.IsFailed)
        {
            return priceResult.ToResult();
        }

        var updateResult = product.UpdatePrice(priceResult.Value, _timeProvider.GetUtcNow());
        if (updateResult.IsFailed)
        {
            return updateResult;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated price for Product {ProductId} to {Amount} {Currency}",
            product.Id, product.Price.Amount, product.Price.Currency.Name);

        return Result.Ok();
    }
}
