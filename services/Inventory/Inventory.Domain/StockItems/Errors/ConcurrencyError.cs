using System.Globalization;
using FluentResults;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Returned by the event-store repository after the retry-once policy is exhausted —
/// another writer appended the next version while this handler was rehydrating.
/// </summary>
/// <remarks>
/// Shape is locked by <c>docs/bc-design/error-taxonomy.md</c> § 3.4.
/// </remarks>
public sealed record ConcurrencyError(Guid StreamId, int ExpectedVersion) : IError
{
    public string Message =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Stream {0} version conflict at {1}.",
            StreamId,
            ExpectedVersion);

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Inventory.Concurrency",
    };

    public List<IError> Reasons { get; } = [];
}
