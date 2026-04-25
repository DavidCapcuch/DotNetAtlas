namespace Inventory.Application.Common.ReadModels;

/// <summary>
/// Read-model row for <c>inventory.current_stock_levels</c> — the hot-path
/// "what's the current stock for ProductId X?" projection described in
/// <c>inventory.md</c> § 9.1.
/// </summary>
/// <remarks>
/// <para>
/// This is a plain denormalised POCO owned by the Application layer; the EF
/// type configuration lives in Infrastructure so the domain has no EF Core
/// dependency.
/// </para>
/// <para>
/// <see cref="PreviousAvailable"/> is not in the design doc; we track it so
/// <c>StockLevelChangedOutboxPublisher</c> can detect <c>0 &lt;-&gt; positive</c>
/// threshold crossings without reconstructing prior state by replay. The field
/// is maintained atomically by <c>CurrentStockLevelsProjectionHandler</c> as
/// the per-event "before" snapshot — see <c>inventory.md</c> § 6.1 for the
/// threshold-emission rule.
/// </para>
/// </remarks>
public sealed class CurrentStockLevelRow
{
    /// <summary>Aggregate identity / stream id.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Units physically present in the warehouse after the last applied event.</summary>
    public int OnHand { get; set; }

    /// <summary>Units committed to active reservations after the last applied event.</summary>
    public int Reserved { get; set; }

    /// <summary>
    /// <see cref="OnHand"/> - <see cref="Reserved"/> after the last applied
    /// event. Materialised (not computed on read) so indexes can target it.
    /// </summary>
    public int Available { get; set; }

    /// <summary>
    /// Value of <see cref="Available"/> BEFORE the last applied event. Enables
    /// the threshold check <c>PreviousAvailable == 0 XOR NewAvailable == 0</c>
    /// used by <c>StockLevelChangedOutboxPublisher</c>.
    /// </summary>
    public int PreviousAvailable { get; set; }

    /// <summary>
    /// UTC timestamp of the last applied event — copied from
    /// <c>event.OccurredOnUtc</c>.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; set; }

    /// <summary>
    /// Monotonic count of events applied to this row, incremented in lockstep
    /// with the aggregate's <c>Version</c>. Reserved for a future replay-based
    /// rebuild path (per <c>inventory.md</c> § 9.3) and for ad-hoc forensics —
    /// NOT consulted on the hot write path because the projection handler runs
    /// inside the same DB transaction as the ES append, so duplicate dispatch
    /// is impossible by construction.
    /// </summary>
    public int LastVersion { get; set; }
}
