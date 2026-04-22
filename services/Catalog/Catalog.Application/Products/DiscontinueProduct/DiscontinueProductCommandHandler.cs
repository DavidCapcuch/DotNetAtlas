using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Catalog.Application.Products.DiscontinueProduct;

public sealed class DiscontinueProductCommandHandler : ICommandHandler<DiscontinueProductCommand>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<DiscontinueProductCommandHandler> _logger;

    public DiscontinueProductCommandHandler(
        ICatalogDbContext db,
        ILogger<DiscontinueProductCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(DiscontinueProductCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        var discontinueResult = product.Discontinue(command.Reason);
        if (discontinueResult.IsFailed)
        {
            return discontinueResult;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Discontinued Product {ProductId} with reason: {Reason}",
            product.Id, command.Reason);

        return Result.Ok();
    }
}
