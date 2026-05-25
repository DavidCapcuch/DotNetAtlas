using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Common.Errors;
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

        // SQL-side projection (ADR-0021 / #277): selects only the columns the response
        // uses, plus PdfBlobName (the only field NOT in the response but needed to mint
        // the SAS URL after materialisation). EF Core 10 translates the owned-collection
        // .Select(...) and the conditional projection of the nullable CancellationInfo VO.
        var row = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.Id == query.InvoiceId)
            .TagWith(nameof(GetInvoiceByIdQueryHandler))
            .Select(InvoiceRow.Projection)
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceNotFound(query.InvoiceId));
        }

        // Ownership enforcement: buyer may read only their own invoice. Return NotFound
        // (not Forbidden) for a cross-buyer lookup so existence is not leaked.
        if (!query.IsAdmin && row.BuyerId != query.BuyerId)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} requested invoice {InvoiceId} owned by a different buyer — returning NotFound.",
                query.BuyerId,
                query.InvoiceId);
            return Result.Fail<GetInvoiceByIdResponse>(InvoicingErrors.InvoiceNotFound(query.InvoiceId));
        }

        // Mint a fresh SAS URL for the persisted PDF (the URL stored on PdfBlobRef has a
        // 10-minute TTL from issuance and is long expired by the time a buyer fetches their
        // invoice). Draft invoices have no PDF yet — leave the URL null. The blob name was
        // computed at upload time via InvoicePdfBlobName.For(invoiceNumber) and stored on
        // PdfBlobRef; the row carries it directly so no re-derivation is needed.
        Uri? sasUrl = null;
        DateTimeOffset? sasExpiresAtUtc = null;
        if (row.PdfBlobName is not null)
        {
            var ttl = TimeSpan.FromMinutes(PdfSasTtlMinutes);
            sasUrl = await _blobStore.GetSasUrlAsync(
                _blobOptions.InvoicesContainerName,
                row.PdfBlobName,
                ttl,
                ct);
            sasExpiresAtUtc = _timeProvider.GetUtcNow().Add(ttl);
        }

        return Result.Ok(row.ToResponse(sasUrl, sasExpiresAtUtc));
    }
}
