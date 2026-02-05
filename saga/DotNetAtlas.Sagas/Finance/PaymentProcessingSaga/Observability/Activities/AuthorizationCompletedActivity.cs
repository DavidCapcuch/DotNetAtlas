using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment authorization completes successfully
/// for the <see cref="PaymentProcessingSaga"/>.
/// </summary>
public sealed class
    AuthorizationCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent>
{
    private readonly ILogger<AuthorizationCompletedActivity> _logger;

    public AuthorizationCompletedActivity(ILogger<AuthorizationCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(AuthorizationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, context.Message.AuthorizationId);
        }

        PaymentProcessingSagaInstrumentation.RecordAuthorizationCompleted();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} authorization completed. AuthId: {AuthorizationId}",
            nameof(PaymentProcessingSaga), saga.CorrelationId, saga.AuthorizationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
