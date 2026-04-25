// Inventory.API - Program.cs
// M5 minimal wire-up: composition root + KafkaFlow consumers.
// ServiceDefaults / FastEndpoints / auth / output-cache / health endpoints
// land in M7 alongside the admin HTTP surface.
using Inventory.Application.Common;
using Inventory.Infrastructure.Common;
using KafkaFlow;
using Platform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment.IsDeployedEnvironment());

var app = builder.Build();

// Skip the Kafka cluster boot in the test host. Integration tests register
// the Kafka handler classes directly and invoke them with synthetic message
// contexts (matching Ordering's M5 precedent at
// test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:19-20);
// booting the consumer here would require Kafka + Schema Registry
// containers, which are deferred to M7's end-to-end slice.
if (!app.Environment.IsTesting())
{
    var kafkaBus = app.Services.CreateKafkaBus();
    await kafkaBus.StartAsync();
}

await app.RunAsync();

/// <summary>
/// Partial <c>Program</c> marker so future integration / functional tests
/// can use <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
