using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;

namespace DotNetAtlas.Api.Common;

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

            app.MapApiClientEndpoint(route, genConfig =>
                {
                    genConfig.SwaggerDocumentName = "v1";
                    genConfig.Language = generationLanguage;
                    genConfig.ClientNamespaceName = "DotNetAtlas";
                    genConfig.ClientClassName = $"{generationLanguage}Client";
                },
                options =>
                {
                    options.CacheOutput(p => p.Expire(TimeSpan.FromDays(365)));
                    options.ExcludeFromDescription();
                });
        }
    }
}
