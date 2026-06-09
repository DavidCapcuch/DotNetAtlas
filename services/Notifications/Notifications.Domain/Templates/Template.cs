namespace Notifications.Domain.Templates;

/// <summary>
/// A notification template — seeded reference data keyed by <see cref="TemplateKey"/>
/// (<c>{bounded-context}.{notification-type}</c>, lower-kebab). Carries only a human-readable
/// <see cref="Description"/>; the per-channel renderable content lives in <see cref="TemplateChannel"/>.
/// Not an aggregate root — immutable seeded reference data with no runtime mutation surface
/// (no HTTP), guarded only by its key. See ADR-0032 § 7 and notifications.md § 7.
/// </summary>
public sealed class Template
{
    private Template(string templateKey, string description)
    {
        TemplateKey = templateKey;
        Description = description;
    }

    // EF Core materialisation constructor.
    private Template()
    {
    }

    /// <summary>Template identity, <c>{bounded-context}.{notification-type}</c> (lower-kebab), e.g. <c>invoicing.invoice-delivered</c>.</summary>
    public string TemplateKey { get; private set; } = null!;

    /// <summary>Human-readable description of the business moment this template notifies about (operator-facing).</summary>
    public string Description { get; private set; } = null!;

    /// <summary>Creates a template reference row (used by the dev seeder and tests).</summary>
    public static Template Create(string templateKey, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new Template(templateKey, description);
    }
}
