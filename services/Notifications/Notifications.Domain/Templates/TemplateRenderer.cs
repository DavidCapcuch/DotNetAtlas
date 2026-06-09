using System.Text.RegularExpressions;

namespace Notifications.Domain.Templates;

/// <summary>
/// Pure <c>{{token}}</c> renderer (ADR-0032 § 7). Substitutes each <c>{{key}}</c> in a template
/// channel's subject/body with the matching <c>Payload</c> value. Deliberately a dumb token-replace
/// — no expression/loop/engine support (Scriban/Razor stays a deferred seam, § 13).
/// </summary>
/// <remarks>
/// <b>Unknown-token contract (pinned):</b> a <c>{{key}}</c> with no payload entry is left
/// <i>literal</i> in the output rather than blanked or rejected. This is the most debuggable
/// choice — a missing field shows up verbatim in the rendered mail instead of silently vanishing —
/// and keeps downstream tests deterministic regardless of payload completeness.
/// </remarks>
public static partial class TemplateRenderer
{
    // Matches {{ Key }} with optional inner whitespace; the key is letters/digits/underscore
    // (payload keys are identifier-shaped, e.g. InvoiceNumber). CultureInvariant: token matching
    // must not vary by current culture.
    [GeneratedRegex(@"\{\{\s*(?<key>[A-Za-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Renders <paramref name="template"/> by replacing every <c>{{key}}</c> token with its
    /// <paramref name="payload"/> value. Tokens absent from the payload are left literal.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> payload)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(payload);

        return TokenRegex().Replace(
            template,
            match => payload.TryGetValue(match.Groups["key"].Value, out var value)
                ? value
                : match.Value);
    }

    /// <summary>
    /// Returns the distinct <c>{{token}}</c> keys still present in already-rendered text — the tokens
    /// <see cref="Render"/> left literal because the payload had no value for them. Empty = fully
    /// rendered. A caller that must not emit a half-rendered message (the email dispatcher) uses this
    /// to fail loudly instead of sending literal <c>{{…}}</c> to a recipient.
    /// </summary>
    public static IReadOnlyCollection<string> FindUnresolvedTokens(string rendered)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        var matches = TokenRegex().Matches(rendered);
        if (matches.Count == 0)
        {
            return [];
        }

        return matches
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
