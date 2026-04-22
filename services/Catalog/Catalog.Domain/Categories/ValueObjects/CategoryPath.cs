using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Catalog.Domain.Categories.Errors;
using FluentResults;
using Platform.SharedKernel.Base;

namespace Catalog.Domain.Categories.ValueObjects;

/// <summary>
/// Materialized path for a <see cref="Category"/> (e.g., <c>/electronics/computers/laptops</c>).
/// Leading slash, lowercase slug segments separated by <c>/</c>, max depth 5.
/// </summary>
public sealed partial record CategoryPath : ValueObject
{
    public const int MaxDepth = 5;

    public string Value { get; private init; } = string.Empty;

    private CategoryPath()
    {
    }

    /// <summary>
    /// Parses and validates a materialized path value.
    /// </summary>
    public static Result<CategoryPath> Create(string? value)
    {
        if (string.IsNullOrEmpty(value) || !PathPattern().IsMatch(value))
        {
            return Result.Fail(CategoryPathErrors.Malformed());
        }

        return new CategoryPath { Value = value };
    }

    /// <summary>
    /// Returns the number of slug segments in the path.
    /// </summary>
    public int Depth() => Value.Count(c => c == '/');

    /// <summary>
    /// Appends a single slug segment, producing a new <see cref="CategoryPath"/>.
    /// Fails if the resulting depth would exceed <see cref="MaxDepth"/> or if the slug is invalid.
    /// </summary>
    public Result<CategoryPath> Append(string slug)
    {
        if (Depth() >= MaxDepth)
        {
            return Result.Fail(CategoryPathErrors.MaxDepthExceeded(MaxDepth));
        }

        return Create($"{Value}/{slug}");
    }

    /// <summary>
    /// Composes a human-readable breadcrumb string from a slug → display-name map.
    /// Unknown slugs fall back to the slug itself.
    /// </summary>
    public string Breadcrumb(IReadOnlyDictionary<string, string> slugToName)
    {
        ArgumentNullException.ThrowIfNull(slugToName);

        var segments = Value.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" > ", segments.Select(s => slugToName.TryGetValue(s, out var n) ? n : s));
    }

    /// <summary>
    /// Slugifies <paramref name="name"/> into a lowercase, dash-delimited, alphanumeric-only token
    /// suitable for a <see cref="CategoryPath"/> segment. Returns <c>null</c> when the result is empty.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Path slugs are lowercase by domain contract (regex [a-z0-9-]); uppercase normalisation would violate the invariant.")]
    public static string? Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var lower = name.Trim().ToLowerInvariant();
        var buffer = new StringBuilder(lower.Length);
        var lastWasDash = true;
        foreach (var ch in lower)
        {
            // ASCII-only — the CategoryPath regex accepts [a-z0-9-].
            // Accented / non-ASCII chars become dash separators so the slug
            // still maps to a validatable path (and users get "accented text"
            // normalised to its ASCII skeleton rather than a Malformed error).
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                buffer.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                buffer.Append('-');
                lastWasDash = true;
            }
        }

        var slug = buffer.ToString().Trim('-');
        return slug.Length == 0 ? null : slug;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^(/[a-z0-9][a-z0-9-]*){1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
