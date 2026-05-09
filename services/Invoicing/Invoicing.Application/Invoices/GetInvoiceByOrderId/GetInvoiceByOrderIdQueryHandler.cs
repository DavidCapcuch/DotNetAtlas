using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Invoices.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoiceByOrderId;

internal sealed class GetInvoiceByOrderIdQueryHandler
    : IQueryHandler<GetInvoiceByOrderIdQuery, GetInvoiceByIdResponse>
{
    private const int PdfSasTtlMinutes = 10;

    private readonly IInvoicingDbContext _dbContext;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetInvoiceByOrderIdQueryHandler> _logger;

    public GetInvoiceByOrderIdQueryHandler(
        IInvoicingDbContext dbContext,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider,
        ILogger<GetInvoiceByOrderIdQueryHandler> logger)
    {
        _dbContext = dbContext;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<GetInvoiceByIdResponse>> HandleAsync(
        GetInvoiceByOrderIdQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .WithSpecification(new InvoiceByOrderIdSpec(query.OrderId))
            .FirstOrDefaultAsync(ct);

        if (invoice is null)
        {
            // No invoice for this order — surface the 404 keyed off the OrderId so the
            // problem-details payload identifies the actual lookup input. Reuses
            // InvoiceNotFound's error code so the HTTP mapping still routes to 404.
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceForOrderNotFound(query.OrderId));
        }

        if (!query.IsAdmin && invoice.BuyerId != query.BuyerId)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} requested invoice for order {OrderId} owned by a different buyer — returning NotFound.",
                query.BuyerId,
                query.OrderId);
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceForOrderNotFound(query.OrderId));
        }

        Uri? sasUrl = null;
        DateTimeOffset? sasExpiresAtUtc = null;
        if (invoice.PdfBlobRef is not null && invoice.InvoiceNumber is not null)
        {
            var ttl = TimeSpan.FromMinutes(PdfSasTtlMinutes);
            sasUrl = await _blobStore.GetSasUrlAsync(
                _blobOptions.InvoicesContainerName,
                InvoicePdfBlobName.For(invoice.InvoiceNumber),
                ttl,
                ct);
            sasExpiresAtUtc = _timeProvider.GetUtcNow().Add(ttl);
        }

        return Result.Ok(InvoiceProjection.ToResponse(invoice, sasUrl, sasExpiresAtUtc));
    }
}
