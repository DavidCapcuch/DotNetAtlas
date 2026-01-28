using DotNetAtlas.CQS;

namespace DotNetAtlas.Application.WeatherAlerts.ProcessExpiredSubscriptions;

public class ProcessExpiredSubscriptionsCommand : ICommand
{
    /// <summary>
    /// Maximum number of expired subscriptions to process in this execution.
    /// </summary>
    public required int BatchSize { get; init; }
}
