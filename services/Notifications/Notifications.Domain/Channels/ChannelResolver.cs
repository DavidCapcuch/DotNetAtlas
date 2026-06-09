namespace Notifications.Domain.Channels;

/// <summary>
/// The channel-resolution rule (notifications.md § 5.3): <c>resolved = enabled_channels ∩ template_channels</c>.
/// A notification fires on a channel only when the recipient enabled it <b>and</b> the template has content
/// for it. There is no mandatory-channel floor (deferred seam, notifications.md § 13), so an empty result —
/// the recipient disabled every channel the template supports — is a valid outcome, not an error.
/// </summary>
public static class ChannelResolver
{
    /// <summary>
    /// Intersects the recipient's <paramref name="enabledChannels"/> with the template's
    /// <paramref name="supportedChannels"/>. The result is de-duplicated and ordered canonically by
    /// <see cref="ChannelType.Value"/> so fan-out enqueues in a deterministic order.
    /// </summary>
    public static IReadOnlyList<ChannelType> Resolve(
        IEnumerable<ChannelType> enabledChannels,
        IEnumerable<ChannelType> supportedChannels)
    {
        ArgumentNullException.ThrowIfNull(enabledChannels);
        ArgumentNullException.ThrowIfNull(supportedChannels);

        var supported = supportedChannels.ToHashSet();

        return enabledChannels
            .Where(supported.Contains)
            .Distinct()
            .OrderBy(channel => channel.Value)
            .ToArray();
    }
}
