namespace DotNetAtlas.Api.Common.Authentication
{
    public static class AuthConfigSections
    {
        public static class Full
        {
            public const string JWT_BEARER = $"{Parts.AUTHENTICATION}:{Parts.JWT_BEARER}";

            public const string OPEN_API_INFO = $"{Parts.SWAGGER}:{Parts.OPEN_API_INFO}";
            public const string O_AUTH_CONFIG = $"{Parts.SWAGGER}:{Parts.O_AUTH_CONFIG}";
        }

        private static class Parts
        {
            public const string AUTHENTICATION = "Authentication";
            public const string JWT_BEARER = "JwtBearer";

            public const string SWAGGER = "Swagger";
            public const string OPEN_API_INFO = "OpenApiInfo";
            public const string O_AUTH_CONFIG = "OAuthConfig";
        }
    }
}
