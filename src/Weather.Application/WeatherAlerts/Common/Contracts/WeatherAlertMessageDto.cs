using MessagePack;

namespace Weather.Application.WeatherAlerts.Common.Contracts;

[MessagePackObject]
public sealed record WeatherAlertMessageDto(
    [property: Key(0)] string Message
);
