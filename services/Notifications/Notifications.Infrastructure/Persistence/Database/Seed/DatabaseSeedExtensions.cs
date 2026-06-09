using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Channels;
using Notifications.Domain.Templates;
using OpenTelemetry;
using Serilog;

namespace Notifications.Infrastructure.Persistence.Database.Seed;

/// <summary>
/// Dev/compose seeding of the notification templates (ADR-0032 § 7). EF Core invokes these during
/// <c>MigrateAsync</c> / <c>dotnet ef database update</c> — the Development fast-iteration path only.
/// Non-Development environments apply schema out-of-band (Evolve in Testing, Flyway in deployed) and
/// never call seeding, so integration tests arrange their own templates per-fixture. Seed-if-empty
/// over fixed reference data (deterministic literals — no Bogus; these are real templates, not fakes).
/// </summary>
/// <remarks>See https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding.</remarks>
public static class DatabaseSeedExtensions
{
    /// <summary>Wires async seeding (fired by <c>MigrateAsync</c>).</summary>
    public static DbContextOptionsBuilder UseAsyncSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseAsyncSeeding(async (dbContext, _, ct) => await SeedTemplatesAsync(dbContext, ct));
        return builder;
    }

    /// <summary>Wires sync seeding (fired by <c>dotnet ef database update</c>).</summary>
    public static DbContextOptionsBuilder UseSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseSeeding((dbContext, _) => SeedTemplatesAsync(dbContext).GetAwaiter().GetResult());
        return builder;
    }

    private static async Task SeedTemplatesAsync(DbContext dbContext, CancellationToken ct = default)
    {
        using var _ = SuppressInstrumentationScope.Begin();

        var db = (NotificationsDbContext)dbContext;
        if (await db.Templates.AnyAsync(ct))
        {
            return;
        }

        var templates = BuildSeedTemplates();
        var channels = BuildSeedChannels();

        db.Templates.AddRange(templates);
        db.TemplateChannels.AddRange(channels);
        await db.SaveChangesAsync(ct);

        Log.Logger.Information(
            "Seeded {TemplateCount} notification template(s) with {ChannelCount} channel row(s)",
            templates.Count,
            channels.Count);
    }

    private static List<Template> BuildSeedTemplates() =>
    [
        Template.Create("invoicing.invoice-delivered", "Sent to a buyer when their invoice is issued and ready to view."),
        Template.Create("order.shipped", "Sent to a buyer when their order ships (demonstrates multi-channel fan-out)."),
    ];

    private static List<TemplateChannel> BuildSeedChannels() =>
    [
        // invoicing.invoice-delivered → [Email] (preserves the live Invoicing Issued → Delivered flow).
        TemplateChannel.Create(
            "invoicing.invoice-delivered",
            ChannelType.Email,
            subject: "Invoice {{InvoiceNumber}} — your copy is ready",
            body: """
                  Hello,

                  Your invoice {{InvoiceNumber}} is ready.
                  Total: {{TotalAmount}} {{Currency}}
                  Sign in to view & download: {{ViewInvoiceUrl}}
                  """),

        // order.shipped → [Email, Bell, Sms]. No production producer in v2 scope (seeded data +
        // test-only NotifyUserCommand); ready for the SMS/bell slices (#315/#316).
        TemplateChannel.Create(
            "order.shipped",
            ChannelType.Email,
            subject: "Your order {{OrderNumber}} has shipped",
            body: """
                  Hi,

                  Your order {{OrderNumber}} is on its way.
                  Track it here: {{TrackingUrl}}
                  """),
        TemplateChannel.Create(
            "order.shipped",
            ChannelType.Bell,
            subject: null,
            body: "Order {{OrderNumber}} has shipped."),
        TemplateChannel.Create(
            "order.shipped",
            ChannelType.Sms,
            subject: null,
            body: "Your order {{OrderNumber}} shipped. Track: {{TrackingUrl}}"),
    ];
}
