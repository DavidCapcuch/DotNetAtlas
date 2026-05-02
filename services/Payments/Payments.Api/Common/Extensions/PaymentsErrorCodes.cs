namespace Payments.Api.Common.Extensions;

/// <summary>
/// Stringly-typed mirror of the <c>errorCode</c> values produced by
/// <c>Payments.Domain.Errors.PaymentsErrors</c>. Lives in the API layer
/// because the mapping it drives — error-code → HTTP status — is an HTTP
/// concern; the Domain layer's error factories do not (and must not) know
/// about HTTP status codes.
/// </summary>
/// <remarks>
/// Keep these constants in sync with
/// <c>services/Payments/Payments.Domain/Errors/PaymentsErrors.cs</c>.
/// </remarks>
internal static class PaymentsErrorCodes
{
    /// <summary>Maps to HTTP 404 Not Found.</summary>
    public const string PaymentNotFound = "Payments.NotFound";
}
