using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Transactions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.UnitTests.Application.Common;

/// <summary>
/// Shared fixture for Payments command-handler unit tests. One instance per test (xUnit
/// constructs a new test class per fact), giving every test a pristine InMemory database plus
/// fresh gateway / outbox / dispatcher substitutes.
/// </summary>
/// <remarks>
/// Replaces the former mocked-repository seam (ADR-0022 removed the hand-rolled persistence
/// repository): handlers now load the aggregate directly off <see cref="IPaymentsDbContext"/>
/// (PK lookups inline, CorrelationId via <c>PaymentByCorrelationIdSpec</c>), so tests seed the
/// <c>Transactions</c> set and let the real InMemory query resolve it.
/// </remarks>
public abstract class PaymentsHandlerTestBase : IDisposable
{
    protected PaymentsHandlerTestBase()
    {
        TimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));

        var options = new DbContextOptionsBuilder<TestPaymentsDbContext>()
            .UseInMemoryDatabase($"payments-tests-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        DbContext = new TestPaymentsDbContext(options);

        Gateway = Substitute.For<IPaymentGateway>();
        Outbox = Substitute.For<ITransactionalOutbox<IPaymentsDbContext>>();
        Dispatcher = Substitute.For<IDomainEventDispatcher>();
    }

    protected FakeTimeProvider TimeProvider { get; }

    protected TestPaymentsDbContext DbContext { get; }

    protected IPaymentGateway Gateway { get; }

    protected ITransactionalOutbox<IPaymentsDbContext> Outbox { get; }

    protected IDomainEventDispatcher Dispatcher { get; }

    /// <summary>
    /// Persists an already-built aggregate so a handler's load (tracked, by Id or CorrelationId)
    /// resolves it through the identity map. The seeded instance stays tracked, so handler
    /// mutations are observable on the same reference the test holds.
    /// </summary>
    protected async Task SeedAsync(PaymentTransaction transaction)
    {
        DbContext.Transactions.Add(transaction);
        await DbContext.SaveChangesAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
