using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment authorization times out
/// for the <see cref="PaymentProcessingSaga"/>.
/// </summary>
public sealed class
    AuthorizationTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, AuthorizationTimeoutExpired>
{
    private readonly ILogger<AuthorizationTimeoutActivity> _logger;

    public AuthorizationTimeoutActivity(ILogger<AuthorizationTimeoutActivity> logger)
    {
        _logger = logger;
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
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(AuthorizationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "authorization");
        }

        PaymentProcessingSagaInstrumentation.RecordSagaTimeout("authorization", duration);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} authorization timed out for user {UserId}",
            nameof(PaymentProcessingSaga), saga.CorrelationId, saga.UserId);

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
