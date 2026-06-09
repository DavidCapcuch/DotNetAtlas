using Microsoft.EntityFrameworkCore;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using OpenTelemetry;
using Serilog;

namespace Notifications.Infrastructure.Persistence.Database.Seed;

/// <summary>
/// Dev/compose seeding of the notification reference tables — templates (ADR-0032 § 7) and recipient
/// preferences (notifications.md § 8). EF Core invokes these during <c>MigrateAsync</c> /
/// <c>dotnet ef database update</c> — the Development fast-iteration path only. Non-Development
/// environments apply schema out-of-band (Evolve in Testing, Flyway in deployed) and never call seeding,
/// so integration tests arrange their own templates/preferences per-fixture. Seed-if-empty over fixed
/// reference data (deterministic literals — no Bogus; these are real templates + real recipients, not fakes).
/// </summary>
/// <remarks>See https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding.</remarks>
public static class DatabaseSeedExtensions
{
    /// <summary>Wires async seeding (fired by <c>MigrateAsync</c>).</summary>
    public static DbContextOptionsBuilder UseAsyncSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseAsyncSeeding(async (dbContext, _, ct) => await SeedReferenceDataAsync(dbContext, ct));
        return builder;
    }

    /// <summary>Wires sync seeding (fired by <c>dotnet ef database update</c>).</summary>
    public static DbContextOptionsBuilder UseSeeding(this DbContextOptionsBuilder builder)
    {
        builder.UseSeeding((dbContext, _) => SeedReferenceDataAsync(dbContext).GetAwaiter().GetResult());
        return builder;
    }

    private static async Task SeedReferenceDataAsync(DbContext dbContext, CancellationToken ct = default)
    {
        using var _ = SuppressInstrumentationScope.Begin();

        var db = (NotificationsDbContext)dbContext;

        await SeedTemplatesAsync(db, ct);
        await SeedPreferencesAsync(db, ct);
    }

    private static async Task SeedTemplatesAsync(NotificationsDbContext db, CancellationToken ct)
    {
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

    private static async Task SeedPreferencesAsync(NotificationsDbContext db, CancellationToken ct)
    {
        if (await db.UserPreferences.AnyAsync(ct))
        {
            return;
        }

        var preferences = BuildSeedPreferences();

        db.UserPreferences.AddRange(preferences);
        await db.SaveChangesAsync(ct);

        Log.Logger.Information("Seeded {PreferenceCount} notification preference(s)", preferences.Count);
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

    /// <summary>
    /// The four Keycloak realm users (subs sourced from <c>src/keycloak/realm-export.json</c>) with
    /// deliberate variety so channel resolution + quiet hours are demoable (notifications.md § 8):
    /// admin/dev all-channels-on, no quiet hours; pleb with <b>Sms disabled</b> (the <c>∩</c> suppressing a
    /// channel); d.capcuch all-on with a <b>22:00–07:00 Europe/Prague</b> window (SMS quiet-hours deferral,
    /// #315). Real emails so Mailpit shows recognizable recipients.
    /// </summary>
    internal static List<NotificationPreference> BuildSeedPreferences()
    {
        const string pragueTimeZone = "Europe/Prague";
        var allChannels = new[] { ChannelType.Email, ChannelType.Sms, ChannelType.Bell };

        return
        [
            NotificationPreference.Create(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                email: "admin@dotnetatlas.com",
                phoneNumber: "+420600000001",
                enabledChannels: allChannels,
                quietHoursStart: null,
                quietHoursEnd: null,
                timeZone: pragueTimeZone),

            NotificationPreference.Create(
                Guid.Parse("00000000-0000-0000-0000-111111111111"),
                email: "dev@dotnetatlas.com",
                phoneNumber: "+420600001111",
                enabledChannels: allChannels,
                quietHoursStart: null,
                quietHoursEnd: null,
                timeZone: pragueTimeZone),

            // Sms OFF — demonstrates enabled ∩ template_channels suppressing a channel the template supports.
            NotificationPreference.Create(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                email: "pleb@dotnetatlas.com",
                phoneNumber: "+420600000002",
                enabledChannels: [ChannelType.Email, ChannelType.Bell],
                quietHoursStart: null,
                quietHoursEnd: null,
                timeZone: pragueTimeZone),

            // All channels on + a quiet-hours window — demonstrates SMS deferral (#315).
            NotificationPreference.Create(
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                email: "d.capcuch@gmail.com",
                phoneNumber: "+420600000003",
                enabledChannels: allChannels,
                quietHoursStart: new TimeOnly(22, 0),
                quietHoursEnd: new TimeOnly(7, 0),
                timeZone: pragueTimeZone),
        ];
    }
}
