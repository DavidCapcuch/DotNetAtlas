using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Catalog.Application.Products.ReactivateProduct;

public sealed class ReactivateProductCommandHandler : ICommandHandler<ReactivateProductCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ReactivateProductCommandHandler> _logger;

    public ReactivateProductCommandHandler(
        ICatalogDbContext db,
        ILogger<ReactivateProductCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReactivateProductCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        var reactivateResult = product.Reactivate(command.AdminReactivation);
        if (reactivateResult.IsFailed)
        {
            return reactivateResult;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Reactivated Product {ProductId}", product.Id);

        return Result.Ok();
    }
}
