using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Ordering.UnitTests.Application.Common;

/// <summary>
/// Shared fixture for Application-layer handler + publisher tests. One
/// instance per test (xUnit constructs a new test class per fact), which
/// gives every test a pristine InMemory database + outbox mock.
/// </summary>
public abstract class HandlerTestBase : IDisposable
{
    /// <summary>
    /// Scale pinned by every money field across the <c>ordering.orders</c> Avro contracts
    /// (<c>decimal(19,4)</c>). Asserted explicitly because a <c>(decimal)</c> cast erases scale.
    /// </summary>
    protected const int MoneyScale = 4;

    protected HandlerTestBase()
    {
        TimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 22, 10, 0, 0, TimeSpan.Zero));

        var options = new DbContextOptionsBuilder<TestOrderingDbContext>()
            .UseInMemoryDatabase($"ordering-tests-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        DbContext = new TestOrderingDbContext(options);

        Outbox = Substitute.For<ITransactionalOutbox<IOrderingDbContext>>();

        TopicsOptions = Options.Create(new TopicsOptions
        {
            OrderingOrders = "ordering.orders",
            OrderCommands = "ordering.order-commands",
            DltTopicSuffix = ".DLT",
        });
    }

    protected FakeTimeProvider TimeProvider { get; }

    protected TestOrderingDbContext DbContext { get; }

    protected ITransactionalOutbox<IOrderingDbContext> Outbox { get; }

    protected IOptions<TopicsOptions> TopicsOptions { get; }

    protected NullLogger<T> Logger<T>() => NullLogger<T>.Instance;

    public void Dispose()
    {
        DbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
