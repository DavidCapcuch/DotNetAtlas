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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DiscontinueProductCommandHandler> _logger;

    public DiscontinueProductCommandHandler(
        ICatalogDbContext db,
        TimeProvider timeProvider,
        ILogger<DiscontinueProductCommandHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(DiscontinueProductCommand command, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == command.ProductId, ct);
        if (product is null)
        {
            return Result.Fail(ProductErrors.NotFound(command.ProductId));
        }

        var discontinueResult = product.Discontinue(command.Reason, _timeProvider.GetUtcNow());
        if (discontinueResult.IsFailed)
        {
            return discontinueResult;
        }

        await _db.SaveChangesAsync(ct);

        // CAT-RV-L01 / #207 (Wave-1 closeout): the operator-supplied reason is free text and
        // unlikely-but-possibly contains PII. Keep it out of structured logs; downstream
        // consumers can read the reason from the outbox-published Avro event if they need it.
        _logger.LogInformation(
            "Discontinued Product {ProductId}",
            product.Id);

        return Result.Ok();
    }
}
