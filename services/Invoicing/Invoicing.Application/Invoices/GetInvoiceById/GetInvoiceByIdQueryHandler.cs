using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Invoices.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoiceById;

internal sealed class GetInvoiceByIdQueryHandler : IQueryHandler<GetInvoiceByIdQuery, GetInvoiceByIdResponse>
{
    /// <summary>
    /// SAS TTL for buyer-fetched invoice PDFs (ADR-0017 § Implementation Notes).
    /// </summary>
    private const int PdfSasTtlMinutes = 10;

    private readonly IInvoicingDbContext _dbContext;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetInvoiceByIdQueryHandler> _logger;

    public GetInvoiceByIdQueryHandler(
        IInvoicingDbContext dbContext,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider,
        ILogger<GetInvoiceByIdQueryHandler> logger)
    {
        _dbContext = dbContext;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<GetInvoiceByIdResponse>> HandleAsync(
        GetInvoiceByIdQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .WithSpecification(new InvoiceByIdSpec(query.InvoiceId))
            .FirstOrDefaultAsync(ct);

        if (invoice is null)
        {
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceNotFound(query.InvoiceId));
        }

        // Ownership enforcement: buyer may read only their own invoice. Return NotFound
        // (not Forbidden) for a cross-buyer lookup so existence is not leaked.
        if (!query.IsAdmin && invoice.BuyerId != query.BuyerId)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} requested invoice {InvoiceId} owned by a different buyer — returning NotFound.",
                query.BuyerId,
                query.InvoiceId);
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceNotFound(query.InvoiceId));
        }

        // Mint a fresh SAS URL for the persisted PDF (the URL stored on PdfBlobRef has a
        // 10-minute TTL from issuance and is long expired by the time a buyer fetches their
        // invoice). Draft invoices have no PDF yet — leave the URL null.
        Uri? sasUrl = null;
        DateTimeOffset? sasExpiresAtUtc = null;
        if (invoice.PdfBlobRef is not null && invoice.InvoiceNumber is not null)
        {
            var ttl = TimeSpan.FromMinutes(PdfSasTtlMinutes);
            var blobName = InvoicePdfBlobName.For(invoice.InvoiceNumber);
            sasUrl = await _blobStore.GetSasUrlAsync(
                _blobOptions.InvoicesContainerName,
                blobName,
                ttl,
                ct);
            sasExpiresAtUtc = _timeProvider.GetUtcNow().Add(ttl);
        }

        return Result.Ok(InvoiceProjection.ToResponse(invoice, sasUrl, sasExpiresAtUtc));
    }
}
