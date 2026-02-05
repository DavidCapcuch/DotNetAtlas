using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the <see cref="PaymentProcessingSaga"/> starts
/// processing a new payment request.
/// </summary>
public sealed class
    PaymentSagaStartedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentInitiatedSagaEvent>
{
    private readonly ILogger<PaymentSagaStartedActivity> _logger;

    public PaymentSagaStartedActivity(ILogger<PaymentSagaStartedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(PaymentSagaStartedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.Amount, saga.Amount);
            activity.SetTag(PaymentSagaActivityTags.Currency, saga.Currency);
        }

        PaymentProcessingSagaInstrumentation.RecordSagaStarted(saga.Currency);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} initialized for user {UserId}, amount {Amount} {Currency}",
            nameof(PaymentProcessingSaga), saga.CorrelationId, saga.UserId, saga.Amount, saga.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentInitiatedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
