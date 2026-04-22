// Catalog.API — Program.cs
// Minimal scaffold (milestone M1). Subsequent milestones wire ServiceDefaults,
// FastEndpoints, EF Core, KafkaFlow, feature flags, and idempotency.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Catalog.API — scaffolded; implementation pending milestones M2–M9.");

await app.RunAsync();
