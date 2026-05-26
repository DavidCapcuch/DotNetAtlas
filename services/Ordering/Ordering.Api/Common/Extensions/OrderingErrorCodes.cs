namespace Ordering.Api.Common.Extensions;

/// <summary>
/// Stringly-typed mirror of the <c>errorCode</c> values produced by
/// <c>Ordering.Domain.Errors.OrderingErrors</c>. Lives in the API layer
/// because the mapping it drives — error-code → HTTP status — is an HTTP
/// concern; the Domain layer's error factories do not (and must not) know
/// about HTTP status codes.
/// </summary>
/// <remarks>
/// Keep these constants in sync with
/// <c>services/Ordering/Ordering.Domain/Errors/OrderingErrors.cs</c>. The
/// arch-test slice in M6 will pin a one-shot reflection assertion: every
/// constant in this class must correspond to a factory method in
/// <c>OrderingErrors</c>.
/// </remarks>
internal static class OrderingErrorCodes
{
    /// <summary>Maps to HTTP 404 Not Found.</summary>
    public const string OrderNotFound = "Order.NotFound";

    /// <summary>Maps to HTTP 409 Conflict (FSM rejection — I-12).</summary>
    public const string CannotCancelInStatus = "Order.CannotCancelInStatus";
}
