namespace DotNetAtlas.Sagas.Common;

public static class InfrastructureDependencyInjection
{
    public static WebApplicationBuilder AddInfrastructure(
        this WebApplicationBuilder builder,
        bool isClusterEnvironment)
    {
        builder.UseSerilogInternal(isClusterEnvironment);
        builder.Services.AddOpenTelemetryInternal(isClusterEnvironment, builder.Configuration);
        builder.Services.AddSagaStateObservability();
        builder.Services.AddHealthChecksInternal(builder.Configuration);

        return builder;
    }
}
