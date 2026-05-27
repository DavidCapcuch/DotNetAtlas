namespace SagaOrchestrators.Common;

public static class InfrastructureDependencyInjection
{
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetryInternal(builder.Configuration);
        builder.Services.AddSagaStateObservability();
        builder.Services.AddSagaHealthChecks(builder.Configuration);

        return builder;
    }
}
