namespace DotNetAtlas.Contracts.ApiContracts.Requests
{
    public class WeatherForecastRequest
    {
        /// <summary>
        /// Number of days of forecast. (1-14)
        /// </summary>
        public required int Days { get; set; }
    }
}