using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment authorization times out
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    AuthorizationTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, AuthorizationTimeoutExpired>
{
    private readonly ILogger<AuthorizationTimeoutActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public AuthorizationTimeoutActivity(
        ILogger<AuthorizationTimeoutActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, AuthorizationTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, AuthorizationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(AuthorizationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, PaymentProcessingSagaErrorCodes.AuthorizationTimeout);
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, PaymentSagaActivityTags.TimeoutStages.Authorization);
        }

        PaymentProcessingSagaMetrics.RecordSagaTimeout(PaymentSagaActivityTags.TimeoutStages.Authorization, duration);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} authorization timed out for user {UserId}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, AuthorizationTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, AuthorizationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
