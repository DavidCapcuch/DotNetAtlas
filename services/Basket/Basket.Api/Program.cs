// Basket.Api — Program.cs
// Minimal scaffold (milestone M1). Subsequent milestones (M6) wire ServiceDefaults,
// FastEndpoints, correlation ID middleware, JWT bearer auth, idempotency output cache,
// the named "basket" FusionCache against redis-basket, and the Catalog ACL typed HttpClient.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Basket.Api — scaffolded; implementation pending milestones M2–M9.");

await app.RunAsync();
