using MessagePack;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

[MessagePackObject]
public sealed record WeatherAlertMessage(
    [property: Key(0)] string Message
);
