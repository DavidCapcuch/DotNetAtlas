using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Domain.Invoices.Specifications;
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

        var invoices = await _dbContext.Invoices
            .AsNoTracking()
            .WithSpecification(new InvoicesByBuyerSpec(query.BuyerId, query.Skip, query.Take))
            .ToListAsync(ct);

        // Mint one SAS URL per row. The container is one round-trip per blob; for v1's
        // expected page size (≤ 100) this is acceptable. A future optimisation could
        // batch via container SAS or skip URLs for non-issued invoices.
        var ttl = TimeSpan.FromMinutes(PdfSasTtlMinutes);
        var nowUtc = _timeProvider.GetUtcNow();
        var responses = new List<GetInvoiceByIdResponse>(invoices.Count);
        foreach (var invoice in invoices)
        {
            Uri? sasUrl = null;
            DateTimeOffset? sasExpiresAtUtc = null;
            if (invoice.PdfBlobRef is not null && invoice.InvoiceNumber is not null)
            {
                sasUrl = await _blobStore.GetSasUrlAsync(
                    _blobOptions.InvoicesContainerName,
                    InvoicePdfBlobName.For(invoice.InvoiceNumber),
                    ttl,
                    ct);
                sasExpiresAtUtc = nowUtc.Add(ttl);
            }

            responses.Add(InvoiceProjection.ToResponse(invoice, sasUrl, sasExpiresAtUtc));
        }

        return Result.Ok(new GetInvoicesByBuyerResponse
        {
            Invoices = responses,
            Skip = query.Skip,
            Take = query.Take,
        });
    }
}
