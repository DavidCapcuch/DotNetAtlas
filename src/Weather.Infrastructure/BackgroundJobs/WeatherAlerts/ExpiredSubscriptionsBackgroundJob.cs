using Hangfire;
using Microsoft.Extensions.Options;
using Platform.CQRS;
using Weather.Application.WeatherAlerts.ProcessExpiredSubscriptions;
using Weather.Infrastructure.BackgroundJobs.Common;
using Weather.Infrastructure.BackgroundJobs.Config;

namespace Weather.Infrastructure.BackgroundJobs.WeatherAlerts;

[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail, LogEvents = true)]
[DisableConcurrentExecution(60)]
internal sealed class ExpiredSubscriptionsBackgroundJob : IBackgroundJob
{
    public const string JobId = nameof(ExpiredSubscriptionsBackgroundJob);

    private readonly ICommandHandler<ProcessExpiredSubscriptionsCommand> _processExpiredSubscriptionsHandler;
    private readonly ExpiredSubscriptionsJobOptions _options;

    public ExpiredSubscriptionsBackgroundJob(
        ICommandHandler<ProcessExpiredSubscriptionsCommand> processExpiredSubscriptionsHandler,
        IOptions<ExpiredSubscriptionsJobOptions> options)
    {
        _processExpiredSubscriptionsHandler = processExpiredSubscriptionsHandler;
        _options = options.Value;
    }

    public async Task ProcessExpiredSubscriptionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var command = new ProcessExpiredSubscriptionsCommand
        {
            BatchSize = _options.BatchSize
        };

        await _processExpiredSubscriptionsHandler.HandleAsync(command, ct);
    }
}
