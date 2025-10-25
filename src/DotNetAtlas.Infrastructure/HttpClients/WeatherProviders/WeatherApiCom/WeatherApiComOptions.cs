using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;

public sealed class WeatherApiComOptions
{
    public const string Section = "WeatherProviders:WeatherApiCom";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string ApiKey { get; set; }
}
