// Payments.Api — Program.cs
// Minimal scaffold (milestone M1). Subsequent milestones (M5–M6) wire ServiceDefaults,
// FastEndpoints, correlation-id + service-auth middleware, JWT bearer auth for admin
// endpoints under /api/v1/payments/..., health checks, Kafka consumers (4 commands),
// and the PaymentsDbContext against PostgreSQL.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Payments.Api — scaffolded; implementation pending milestones M2–M9.");

await app.RunAsync();
