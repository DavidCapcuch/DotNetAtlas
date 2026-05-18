namespace Catalog.Application.Common.Validation;

/// <summary>
/// Conservative heuristic that flags strings looking like HTML/XML markup so that
/// untrusted free-text fields cannot smuggle script tags, comments, processing
/// instructions, CDATA blocks, or HTML-encoded entities downstream (CAT-SEC-004,
/// Wave-1 closeout). Replaces the earlier "&lt;letter" check which let comments,
/// doctypes, processing instructions, CDATA, and encoded entities through.
/// </summary>
/// <remarks>
/// This is a reject-on-suspicious-token heuristic, not a sanitizer. For richer
/// stripping rules (allow-list of safe tags etc.) reach for <c>Ganss.Xss.HtmlSanitizer</c>.
/// </remarks>
internal static class HtmlHeuristic
{
    public static bool ContainsMarkup(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        for (var i = 0; i < value.Length - 1; i++)
        {
            var c = value[i];
            var next = value[i + 1];

            if (c == '<')
            {
                // Open-tag (<letter), closing-tag (</), comment / doctype / CDATA (<!),
                // processing instruction (<?). Everything else (e.g. "5 < 10") stays innocuous.
                if (char.IsLetter(next) || next is '!' or '?' or '/')
                {
                    return true;
                }
            }
            else if (c == '&')
            {
                // Numeric entity &#nn; or hex entity &#xnn;
                if (next == '#')
                {
                    return true;
                }

                // Named angle-bracket entities &lt; and &gt; (case-insensitive). Stop early
                // if there aren't enough characters left for "&lt;" / "&gt;".
                if (i + 3 < value.Length && value[i + 3] == ';')
                {
                    var a = char.ToLowerInvariant(next);
                    var b = char.ToLowerInvariant(value[i + 2]);
                    if ((a == 'l' || a == 'g') && b == 't')
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
