using FluentResults;
using Invoicing.Application.Blobs;
using Invoicing.Application.Common.Blobs;
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Common.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQRS;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

internal sealed class GetCreditNoteByIdQueryHandler
    : IQueryHandler<GetCreditNoteByIdQuery, GetCreditNoteByIdResponse>
{
    private const int PdfSasTtlMinutes = 10;

    private readonly IInvoicingDbContext _dbContext;
    private readonly IBlobStore _blobStore;
    private readonly BlobStorageOptions _blobOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetCreditNoteByIdQueryHandler> _logger;

    public GetCreditNoteByIdQueryHandler(
        IInvoicingDbContext dbContext,
        IBlobStore blobStore,
        IOptions<BlobStorageOptions> blobOptions,
        TimeProvider timeProvider,
        ILogger<GetCreditNoteByIdQueryHandler> logger)
    {
        _dbContext = dbContext;
        _blobStore = blobStore;
        _blobOptions = blobOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<GetCreditNoteByIdResponse>> HandleAsync(
        GetCreditNoteByIdQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // SQL-side projection via CreditNoteRow.Projection (ADR-0021 / #277).
        var row = await _dbContext.CreditNotes
            .AsNoTracking()
            .Where(cn => cn.Id == query.CreditNoteId)
            .TagWith(nameof(GetCreditNoteByIdQueryHandler))
            .Select(CreditNoteRow.Projection)
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            return Result.Fail<GetCreditNoteByIdResponse>(
                InvoicingErrors.CreditNoteNotFound(query.CreditNoteId));
        }

        if (!query.IsAdmin && row.BuyerId != query.BuyerId)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} requested credit note {CreditNoteId} owned by a different buyer — returning NotFound.",
                query.BuyerId,
                query.CreditNoteId);
            return Result.Fail<GetCreditNoteByIdResponse>(
                InvoicingErrors.CreditNoteNotFound(query.CreditNoteId));
        }

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
