using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment authorization fails
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    AuthorizationFailedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent>
{
    private readonly ILogger<AuthorizationFailedActivity> _logger;

    public AuthorizationFailedActivity(ILogger<AuthorizationFailedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(AuthorizationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(PaymentSagaActivityTags.IsRetryable, message.IsRetryable);
        }

        PaymentProcessingSagaMetrics.RecordAuthorizationFailed(message.ErrorCode);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} authorization failed: {ErrorCode} - {ErrorMessage}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
