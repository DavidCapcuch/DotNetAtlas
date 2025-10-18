using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteo;

public sealed class OpenMeteoOptions
{
    public const string Section = "WeatherProviders:OpenMeteo";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; }

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string GeoBaseUrl { get; set; }
}
