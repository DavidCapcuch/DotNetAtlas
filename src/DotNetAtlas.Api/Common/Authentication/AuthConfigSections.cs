namespace DotNetAtlas.Api.Common.Authentication;

internal static class AuthConfigSections
{
    internal static class Full
    {
        public const string JwtBearer = $"{Parts.Authentication}:{Parts.JwtBearer}";

        public const string OpenApiInfo = $"{Parts.Swagger}:{Parts.OpenApiInfo}";
        public const string OAuthConfig = $"{Parts.Swagger}:{Parts.OAuthConfig}";
    }

    private static class Parts
    {
        public const string Authentication = "Authentication";
        public const string JwtBearer = "JwtBearer";

        public const string Swagger = "Swagger";
        public const string OpenApiInfo = "OpenApiInfo";
        public const string OAuthConfig = "OAuthConfig";
    }
}
