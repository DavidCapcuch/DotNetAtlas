using Platform.SharedKernel.Base;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Raw gateway response code and accompanying human-readable message, captured verbatim from the
/// gateway call. Stored on the aggregate for forensic purposes (e.g., tracing why a transaction
/// took the path it did). Not used for control flow — control flow uses <see cref="FailureReason"/>.
/// </summary>
public sealed record GatewayResponseCode : ValueObject
{
    /// <summary>Gateway-specific code (e.g., <c>"insufficient_funds"</c>, <c>"ok"</c>).</summary>
    public string Code { get; private init; } = null!;

    /// <summary>Gateway's human-readable message for the code.</summary>
    public string Message { get; private init; } = null!;

    private GatewayResponseCode()
    {
    }

    public static GatewayResponseCode Create(string code, string message) =>
        new()
        {
            Code = code,
            Message = message,
        };
}
