using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.GetInvoiceById;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

internal sealed class GetInvoicesByBuyerQueryHandler
    : IQueryHandler<GetInvoicesByBuyerQuery, GetInvoicesByBuyerResponse>
{
    private const int PdfSasTtlMinutes = 10;

    private readonly IInvoicingDbContext _dbContext;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;

    public GetInvoicesByBuyerQueryHandler(
        IInvoicingDbContext dbContext,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<Result<GetInvoicesByBuyerResponse>> HandleAsync(
        GetInvoicesByBuyerQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // SQL-side projection via the shared InvoiceRow.Projection (ADR-0021 / #277).
        // Deterministic paging: primary by issue recency, tie-broken by Id (Guid v7 —
        // time-ordered) so two invoices with equal IssueDate at sub-ms resolution never
        // drop or duplicate across pages.
        var rows = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.BuyerId == query.BuyerId)
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .TagWith(nameof(GetInvoicesByBuyerQueryHandler))
            .Select(InvoiceRow.Projection)
            .ToListAsync(ct);

        // Mint one SAS URL per row. The container is one round-trip per blob; for v1's
        // expected page size (≤ 100) this is acceptable. A future optimisation could
        // batch via container SAS or skip URLs for non-issued invoices.
        var ttl = TimeSpan.FromMinutes(PdfSasTtlMinutes);
        var nowUtc = _timeProvider.GetUtcNow();
        var responses = new List<GetInvoiceByIdResponse>(rows.Count);
        foreach (var row in rows)
        {
            Uri? sasUrl = null;
            DateTimeOffset? sasExpiresAtUtc = null;
            if (row.PdfBlobName is not null)
            {
                sasUrl = await _blobStore.GetSasUrlAsync(
                    _blobOptions.InvoicesContainerName,
                    row.PdfBlobName,
                    ttl,
                    ct);
                sasExpiresAtUtc = nowUtc.Add(ttl);
            }

            responses.Add(row.ToResponse(sasUrl, sasExpiresAtUtc));
        }

        return Result.Ok(new GetInvoicesByBuyerResponse
        {
            Invoices = responses,
            Skip = query.Skip,
            Take = query.Take,
        });
    }
}
