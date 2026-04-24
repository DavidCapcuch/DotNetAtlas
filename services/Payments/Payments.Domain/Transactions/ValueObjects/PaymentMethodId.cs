using FluentResults;
using Payments.Domain.Errors;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Pii;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Gateway-issued token representing a tokenised payment instrument. Never a raw PAN or CVV —
/// PCI scope-minimisation (see <c>docs/bc-design/payments.md § 10</c>). The <see cref="PiiAttribute"/>
/// marker participates in Serilog destructuring + OTel span-attribute redaction per ADR-0011.
/// </summary>
[Pii]
public sealed record PaymentMethodId : ValueObject
{
    private const int MaxLength = 64;

    public string Value { get; }

    private PaymentMethodId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a <see cref="PaymentMethodId"/> after enforcing the 1-64 character contract.
    /// </summary>
    /// <param name="value">Gateway-issued token (non-empty, trimmed length ≤ 64).</param>
    /// <returns>A successful result with the value, or <see cref="PaymentsErrors.InvalidPaymentMethod"/>.</returns>
    public static Result<PaymentMethodId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return Result.Fail<PaymentMethodId>(PaymentsErrors.InvalidPaymentMethod());
        }

        return Result.Ok(new PaymentMethodId(value));
    }

    public override string ToString() => Value;
}
