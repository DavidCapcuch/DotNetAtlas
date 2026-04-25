using Payments.Domain.Errors;
using Payments.Domain.Transactions.ValueObjects;

namespace Payments.Application.Abstractions;

/// <summary>
/// Maps a raw gateway response code (carried on
/// <see cref="GatewayDeclinedError.GatewayCode"/>) to a domain
/// <see cref="FailureReason"/>. Lives in <c>Payments.Application</c> so command handlers can
/// translate gateway-port failures into <see cref="FailureInfo"/> without taking a dependency
/// on a concrete adapter — the M7 architecture test forbids
/// <c>Payments.Application → Payments.Infrastructure</c> imports.
/// </summary>
/// <remarks>
/// Mapping per <c>docs/implementation-prompts/payments.md</c> &lt;example_design_decision&gt;:
/// <list type="bullet">
///   <item><description><c>insufficient_funds</c> → <see cref="FailureReason.InsufficientFunds"/></description></item>
///   <item><description><c>card_declined</c> → <see cref="FailureReason.GatewayDeclined"/></description></item>
///   <item><description><c>fraud_suspected</c> → <see cref="FailureReason.FraudSuspected"/></description></item>
///   <item><description><c>timeout</c> → <see cref="FailureReason.GatewayTimeout"/></description></item>
///   <item><description><c>cancelled_by_user</c> → <see cref="FailureReason.Cancelled"/></description></item>
///   <item><description>anything else (including <see langword="null"/> / empty) → <see cref="FailureReason.Unknown"/></description></item>
/// </list>
/// As real gateway data arrives, grow the table here and add an integration test for the new
/// code; <see cref="FailureReason.Unknown"/> is the deliberate catch-all so unmapped codes
/// stay visible in audit data without breaking the handler.
/// </remarks>
public static class GatewayResponseClassifier
{
    /// <summary>
    /// Classifies a raw gateway response code into a domain <see cref="FailureReason"/>.
    /// </summary>
    /// <param name="code">Raw gateway code (case-sensitive). <see langword="null"/> or
    /// blank → <see cref="FailureReason.Unknown"/>.</param>
    /// <returns>The mapped <see cref="FailureReason"/>.</returns>
    public static FailureReason Classify(string? code) => code switch
    {
        "insufficient_funds" => FailureReason.InsufficientFunds,
        "card_declined" => FailureReason.GatewayDeclined,
        "fraud_suspected" => FailureReason.FraudSuspected,
        "timeout" => FailureReason.GatewayTimeout,
        "cancelled_by_user" => FailureReason.Cancelled,
        _ => FailureReason.Unknown,
    };
}
