using System.Security.Claims;
using System.Text.Json.Serialization;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FastEndpoints;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastQuery : IQuery<GetForecastResponse>
{
    /// <summary>
    /// Number of days of forecast (1-14).
    /// </summary>
    [QueryParam]
    public required int Days { get; set; }

    /// <summary>
    /// City name to fetch forecast for.
    /// </summary>
    [QueryParam]
    public required string City { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code to disambiguate city (e.g., CZ).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [QueryParam]
    public required CountryCode CountryCode { get; set; }

    [FromClaim(ClaimTypes.NameIdentifier, false, true)]
    [HideFromDocs]
    public Guid? UserId { get; set; }
}
