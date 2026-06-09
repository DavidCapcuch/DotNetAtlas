using Notifications.Domain.Channels;

namespace Notifications.Domain.Templates;

/// <summary>
/// The renderable content for one channel of a <see cref="Template"/> — seeded reference data keyed
/// (<see cref="TemplateKey"/>, <see cref="Channel"/>). The set of rows for a template key is also the
/// template's "supported channels" set used by later channel resolution (<c>enabled ∩ template_channels</c>,
/// #314). <see cref="Subject"/> is nullable (only channels with a subject line, e.g. email, set it);
/// <see cref="Body"/> is required. Both carry <c>{{token}}</c> placeholders rendered against the
/// command payload by <see cref="TemplateRenderer"/>. See ADR-0032 § 7 and notifications.md § 7.
/// </summary>
public sealed class TemplateChannel
{
    private TemplateChannel(string templateKey, ChannelType channel, string? subject, string body)
    {
        TemplateKey = templateKey;
        Channel = channel;
        Subject = subject;
        Body = body;
    }

    // EF Core materialisation constructor.
    private TemplateChannel()
    {
    }

    /// <summary>Owning template's key (half of the composite key; FK to <see cref="Template"/>).</summary>
    public string TemplateKey { get; private set; } = null!;

    /// <summary>Channel this content renders for (the other half of the composite key).</summary>
    public ChannelType Channel { get; private set; } = null!;

    /// <summary>Subject-line template with <c>{{token}}</c> placeholders; <c>null</c> for channels without a subject.</summary>
    public string? Subject { get; private set; }

    /// <summary>Body template with <c>{{token}}</c> placeholders. Required.</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Creates a per-channel template row (used by the dev seeder and tests).</summary>
    public static TemplateChannel Create(string templateKey, ChannelType channel, string? subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new TemplateChannel(templateKey, channel, subject, body);
    }
}
