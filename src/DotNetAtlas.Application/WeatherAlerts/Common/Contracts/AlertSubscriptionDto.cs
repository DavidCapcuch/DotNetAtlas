using DotNetAtlas.Domain.Entities.Weather.Forecast;
using MessagePack;

namespace DotNetAtlas.Application.WeatherAlerts.Common.Contracts;

[MessagePackObject]
public sealed record AlertSubscriptionDto(
    [property: Key(0)] string City,
    [property: Key(1)] CountryCode CountryCode
);
