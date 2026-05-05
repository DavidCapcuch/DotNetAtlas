using NSubstitute;
using OpenFeature;
using OpenFeature.Model;
using SagaOrchestrators.Checkout.CheckoutSaga;

namespace SagaOrchestrators.UnitTests.Checkout;

/// <summary>
/// Test-double factory for <see cref="IFeatureClient"/> covering the Checkout saga's flag
/// surface (M8, ADR-0014). NSubstitute pattern mirrors Catalog's
/// <c>SearchProductsQueryHandlerTests.FlagClient(bool)</c>.
/// </summary>
internal static class CheckoutFeatureClientStub
{
    /// <summary>
    /// Returns an <see cref="IFeatureClient"/> mock that resolves
    /// <see cref="CheckoutSagaFeatureFlags.PaymentThenStock"/> to <paramref name="paymentThenStockEnabled"/>
    /// regardless of context / options / cancellation. All other flag keys fall through to
    /// <c>NSubstitute</c>'s default (<c>false</c>), which is the safe default for OFF-by-default
    /// reference flags in this repo.
    /// </summary>
    public static IFeatureClient WithPaymentThenStock(bool paymentThenStockEnabled)
    {
        var client = Substitute.For<IFeatureClient>();
        client.GetBooleanValueAsync(
                CheckoutSagaFeatureFlags.PaymentThenStock,
                Arg.Any<bool>(),
                Arg.Any<EvaluationContext>(),
                Arg.Any<FlagEvaluationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(paymentThenStockEnabled));
        return client;
    }

    /// <summary>
    /// Like <see cref="WithPaymentThenStock"/>, but the returned task hits a real timer-based
    /// continuation before completing. Forces the saga's flag-read step to <b>actually
    /// await</b> rather than running synchronously to completion on a pre-completed
    /// <see cref="Task.FromResult{T}(T)"/>. Use this stub to lock in correct
    /// <c>ThenAsync</c> sequencing: a regression to <c>Then(async …)</c> (which binds as
    /// async-void because <c>Then</c> only takes <c>Action&lt;BehaviorContext&gt;</c>) would
    /// let the IfElse predicate read the <c>[NotMapped]</c> flag default before the
    /// assignment lands. <c>Task.Yield</c> is too weak — under thread-pool scheduling the
    /// resumption may still happen before the IfElse reads the value. <c>Task.Delay</c>
    /// guarantees the schedule, which is what we need to make the regression deterministic.
    /// </summary>
    public static IFeatureClient WithPaymentThenStockAwaiting(bool paymentThenStockEnabled)
    {
        var client = Substitute.For<IFeatureClient>();
        client.GetBooleanValueAsync(
                CheckoutSagaFeatureFlags.PaymentThenStock,
                Arg.Any<bool>(),
                Arg.Any<EvaluationContext>(),
                Arg.Any<FlagEvaluationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return paymentThenStockEnabled;
            });
        return client;
    }
}
