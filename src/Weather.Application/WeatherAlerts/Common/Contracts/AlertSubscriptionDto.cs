using MessagePack;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Application.WeatherAlerts.Common.Contracts;

[MessagePackObject]
public sealed record AlertSubscriptionDto(
    [property: Key(0)] string City,
    [property: Key(1)] CountryCode CountryCode
);
