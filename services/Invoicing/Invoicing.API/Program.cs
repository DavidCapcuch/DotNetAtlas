// Invoicing.API — Program.cs
// Minimal scaffold (milestone M1). Subsequent milestones wire ServiceDefaults,
// FastEndpoints, correlation-id middleware, JWT bearer auth, idempotency output cache
// (redis-cache per ADR-0013), blob storage (Azurite/Azure Blob per ADR-0017), PDF
// generation (QuestPDF per ADR-0019), and the Kafka consumers for the enrichment
// projection (per docs/bc-design/invoicing.md § 8).

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Invoicing.API — scaffolded; implementation pending milestones M2\u2013M10.");

await app.RunAsync();

/// <summary>
/// Partial <c>Program</c> marker so integration / functional tests can use
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
