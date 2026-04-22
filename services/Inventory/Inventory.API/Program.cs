// Inventory.API — Program.cs
// Minimal scaffold (milestone M1). Subsequent milestones wire ServiceDefaults,
// FastEndpoints, EF Core, KafkaFlow, feature flags, and idempotency.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Inventory.API — scaffolded; implementation pending milestones M2–M10.");

await app.RunAsync();
