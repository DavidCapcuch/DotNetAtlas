using DotNetAtlas.Application.Common.CQS;

namespace DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;

public sealed record ConnectionDisconnectCleanupCommand(string ConnectionId) : ICommand;
