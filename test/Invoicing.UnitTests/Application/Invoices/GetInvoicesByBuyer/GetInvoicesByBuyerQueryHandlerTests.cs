using Invoicing.Application.Blobs;
using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Invoicing.UnitTests.Common;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.UnitTests.Application.Invoices.GetInvoicesByBuyer;

/// <summary>
/// Handler-level defence-in-depth pin mirroring the Ordering-side guard
/// added in PR #241. <see cref="GetInvoicesByBuyerQueryValidator"/> is
/// the front-line guard for PageNumber / PageSize; this test bypasses
/// the validation pipeline by constructing the handler directly and
/// asserts that an out-of-range PageNumber / PageSize is rejected with
/// a bug-class <see cref="DataIntegrityException"/> rather than
/// degrading silently (PageSize=0 → empty page; PageNumber=0 → negative
/// EF offset).
/// </summary>
public sealed class GetInvoicesByBuyerQueryHandlerTests : IDisposable
{
    private readonly TestInvoicingDbContext _dbContext = TestInvoicingDbContext.Create();
    private readonly FakeTimeProvider _timeProvider =
        new(new DateTimeOffset(2026, 5, 27, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(0, 20)] // PageNumber=0 → (0-1)*20 = -20 offset, undefined EF behaviour
    [InlineData(-1, 20)] // PageNumber<0 → even more negative offset
    [InlineData(1, 0)] // PageSize=0 → Take(0) silent-empty-page bug class
    [InlineData(1, -5)] // PageSize<0 → Take(<0) undefined behaviour
    [InlineData(1, 101)] // PageSize above MaxPageSize=100 → unbounded query
    public async Task Handle_OutOfRangePageNumberOrPageSize_ThrowsDataIntegrityException(int pageNumber, int pageSize)
    {
        var query = new GetInvoicesByBuyerQuery
        {
            BuyerId = Guid.CreateVersion7(),
            PageNumber = pageNumber,
            PageSize = pageSize,
        };

        var act = () => CreateHandler().HandleAsync(query, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DataIntegrityException>();
        thrown.Which.ErrorCode.Should().Be("InvoicesByBuyer.OutOfRange");
    }

    private GetInvoicesByBuyerQueryHandler CreateHandler()
    {
        // The guard runs before any blob-store call, so a no-op substitute is
        // sufficient; an empty BlobStorageOptions keeps the constructor happy.
        var blobStore = Substitute.For<IBlobStore>();
        var blobOptions = Options.Create(new BlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            InvoicesContainerName = "invoices",
        });
        return new GetInvoicesByBuyerQueryHandler(_dbContext, blobStore, blobOptions, _timeProvider);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
