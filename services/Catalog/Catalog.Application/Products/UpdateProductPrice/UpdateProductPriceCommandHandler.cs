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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateProductPriceCommandHandler> _logger;

    // CAT-RV-L01 / #208: ctor parameter order follows the `(db, TimeProvider, ILogger<T>)`
    // convention shared by every Catalog command handler.
    public UpdateProductPriceCommandHandler(
        ICatalogDbContext db,
        TimeProvider timeProvider,
        ILogger<UpdateProductPriceCommandHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(UpdateProductPriceCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        // ADR-0002: a product's price currency is fixed for its lifetime. A reprice supplies only the
        // amount and reuses the product's existing currency — Money.Create with a non-null CurrencyCode
        // cannot fail, so there is no currency-parse failure to cascade here.
        var newPrice = Money.Create(command.NewAmount, product.Price.Currency).Value;

        var updateResult = product.UpdatePrice(newPrice, _timeProvider.GetUtcNow());
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
