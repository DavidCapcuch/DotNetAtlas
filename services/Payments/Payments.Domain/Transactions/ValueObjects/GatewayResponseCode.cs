using Platform.SharedKernel.Base;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Raw gateway response code and accompanying human-readable message, captured verbatim from the
/// gateway call. Stored on the aggregate for forensic purposes (e.g., tracing why a transaction
/// took the path it did). Not used for control flow — control flow uses <see cref="FailureReason"/>.
/// </summary>
/// <param name="Code">Gateway-specific code (e.g., <c>"insufficient_funds"</c>, <c>"ok"</c>).</param>
/// <param name="Message">Gateway's human-readable message for the code.</param>
public sealed record GatewayResponseCode(string Code, string Message) : ValueObject;
