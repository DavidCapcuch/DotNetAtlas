using FluentResults;

namespace Payments.Domain.Errors;

/// <summary>
/// Business-expected failure error emitted when the payment gateway declines an authorize
/// or capture call (e.g., <c>insufficient_funds</c>, <c>card_declined</c>). The saga consumes
/// the translated external <c>PaymentFailedEvent</c> to drive its compensation branch.
/// Bug-class FSM violations use <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>,
/// not this record — see <c>docs/bc-design/error-taxonomy.md § 3.5</c>.
/// </summary>
/// <param name="Reason">Human-readable reason the gateway provided.</param>
/// <param name="GatewayCode">Raw gateway code (e.g., <c>"insufficient_funds"</c>), if supplied.</param>
public sealed record GatewayDeclinedError(string Reason, string? GatewayCode) : IError
{
    public string Message => $"Payment gateway declined: {Reason}" + (GatewayCode is null ? "" : $" ({GatewayCode}).");

    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Payments.GatewayDeclined" };

    public List<IError> Reasons { get; } = [];
}
