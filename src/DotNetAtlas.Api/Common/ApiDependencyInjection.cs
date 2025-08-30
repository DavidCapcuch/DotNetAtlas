using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;

namespace DotNetAtlas.Api.Common
{
    public static class ApiDependencyInjection
    {
        /// <summary>
        /// Maps client generation APIs for each supported <see cref="GenerationLanguage"/>.
        /// </summary>
        public static void MapClientGenerationApis(this WebApplication app)
        {
            foreach (var generationLanguage in Enum.GetValues<GenerationLanguage>())
            {
                var route = $"/{generationLanguage}";

                app.MapApiClientEndpoint(route, c =>
                    {
                        c.SwaggerDocumentName = "v1";
                        c.Language = generationLanguage;
                        c.ClientNamespaceName = "DotNetAtlas";
                        c.ClientClassName = $"{generationLanguage}Client";
                    },
                    o =>
                    {
                        o.CacheOutput(p => p.Expire(TimeSpan.FromDays(365)));
                        o.ExcludeFromDescription();
                    });
            }
        }
    }
}