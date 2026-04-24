using System.Globalization;
using System.Text.RegularExpressions;
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// Gap-free invoice number per ADR-0018.
/// Format: <c>INV-YYYY-NNNNNN</c> (e.g., <c>INV-2026-000142</c>). Immutable post-allocation (I-3).
/// </summary>
/// <remarks>
/// The sequence part is a <see cref="long"/> internally but rendered as zero-padded 6 digits
/// for the canonical representation. Year is 4-digit AD (1900-9999 accepted by format).
/// </remarks>
/// <param name="Value">Canonical string representation.</param>
public sealed partial record InvoiceNumber(string Value) : ValueObject
{
    private const string Pattern = @"^INV-\d{4}-\d{6}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FormatRegex();

    /// <summary>
    /// Builds an <see cref="InvoiceNumber"/> from the year + sequence allocated by the
    /// transactional allocator (ADR-0018). Invariant: sequence &gt; 0 and \u2264 999999.
    /// </summary>
    public static Result<InvoiceNumber> Create(int year, long sequence)
    {
        if (year is < 1900 or > 9999)
        {
            return Result.Fail<InvoiceNumber>(new ValidationError(
                nameof(year), "Year must be 1900-9999.", "Invoicing.InvalidInvoiceNumberYear"));
        }

        if (sequence is < 1 or > 999999)
        {
            return Result.Fail<InvoiceNumber>(new ValidationError(
                nameof(sequence), "Sequence must be in [1, 999999].", "Invoicing.InvalidInvoiceNumberSequence"));
        }

        var formatted = string.Create(
            CultureInfo.InvariantCulture,
            $"INV-{year:D4}-{sequence:D6}");
        return Result.Ok(new InvoiceNumber(formatted));
    }

    /// <summary>
    /// Rehydrates an <see cref="InvoiceNumber"/> from the persisted canonical string
    /// (used by EF materialization). Validates shape only.
    /// </summary>
    public static Result<InvoiceNumber> FromRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !FormatRegex().IsMatch(raw))
        {
            return Result.Fail<InvoiceNumber>(new ValidationError(
                nameof(raw), "InvoiceNumber must match format INV-YYYY-NNNNNN.", "Invoicing.InvalidInvoiceNumberFormat"));
        }

        return Result.Ok(new InvoiceNumber(raw));
    }

    /// <summary>Extracts the year segment.</summary>
    public int Year => int.Parse(Value.AsSpan(4, 4), CultureInfo.InvariantCulture);

    /// <summary>Extracts the sequence segment.</summary>
    public long Sequence => long.Parse(Value.AsSpan(9, 6), CultureInfo.InvariantCulture);

    public override string ToString() => Value;
}
