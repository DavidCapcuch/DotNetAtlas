using Microsoft.Extensions.Configuration;
using SagaOrchestrators.Common.Config;

namespace SagaOrchestrators.UnitTests.Checkout;

/// <summary>
/// Cross-BC timeout invariant — F4 / ADR-0004 § Implementation Notes /
/// <see href="../bc-design/saga-stuck-runbook.md">saga-stuck-runbook.md § 6</see>.
/// Asserts that the worst-case happy-path-then-compensation cycle does not outlive
/// an Inventory stock reservation:
/// <code>
/// OrderCreationSeconds + StockReservationSeconds + PaymentSeconds + OrderConfirmationSeconds
///   + 2 × CompensationSeconds
///   &lt; InventoryReservationInvariants.InventoryReservationTtlSeconds
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Reads the production <c>Saga:CheckoutTimeouts</c> values from
/// <c>saga/SagaOrchestrators/appsettings.json</c> (NOT the Testing overlay - the invariant
/// is a property of the deployed configuration, and the Testing overlay typically uses
/// uniform short timeouts that would mask a production drift).
/// </para>
/// <para>
/// Per ADR-0026 (capture pivot) the happy path spans two payment waits -- authorization
/// before confirmation and capture after -- so the budget counts 2 x PaymentSeconds.
/// Compensation is single-pass: the refund-then-stock-release split (and its
/// CompensatingPayment rearm of CompensationTimeout) was removed, so a confirmation failure is
/// a pre-capture void + stock release under one CompensationSeconds window.
/// </para>
/// </remarks>
public sealed class CheckoutTimeoutInvariantTests
{
    [Fact]
    public void ProductionTimeoutBudget_PlusTwiceCompensation_StaysUnderInventoryReservationTtl()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var timeouts = configuration
            .GetSection($"{SagaOptions.Section}:CheckoutTimeouts")
            .Get<CheckoutSagaTimeoutOptions>()
            ?? throw new InvalidOperationException(
                "Saga:CheckoutTimeouts missing from saga/SagaOrchestrators/appsettings.json. " +
                "The Checkout saga relies on these values for the F4/ADR-0004 invariant.");

        var happyPathBudgetSeconds =
            timeouts.OrderCreationSeconds
            + timeouts.StockReservationSeconds
            + timeouts.PaymentSeconds
            + timeouts.OrderConfirmationSeconds;
        var compensationBudgetSeconds = 2 * timeouts.CompensationSeconds;
        var totalBudgetSeconds = happyPathBudgetSeconds + compensationBudgetSeconds;

        totalBudgetSeconds.Should().BeLessThan(
            InventoryReservationInvariants.InventoryReservationTtlSeconds,
            "Sum(OrderCreationSeconds={0}, StockReservationSeconds={1}, PaymentSeconds={2}, " +
            "OrderConfirmationSeconds={3}) + 2 × CompensationSeconds({4}) = {5}s must stay under " +
            "Inventory reservation TTL ({6}s) so a worst-case happy-path-then-compensation cycle " +
            "cannot outlive a stock reservation per F4 / ADR-0004",
            timeouts.OrderCreationSeconds,
            timeouts.StockReservationSeconds,
            timeouts.PaymentSeconds,
            timeouts.OrderConfirmationSeconds,
            timeouts.CompensationSeconds,
            totalBudgetSeconds,
            InventoryReservationInvariants.InventoryReservationTtlSeconds);
    }
}
