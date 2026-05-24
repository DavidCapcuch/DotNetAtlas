using System.Globalization;
using System.Text.RegularExpressions;
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.CreditNotes.ValueObjects;

/// <summary>
/// Gap-free credit-note number. Separate sequence from <c>InvoiceNumber</c> (ADR-0018).
/// Format: <c>CN-YYYY-NNNNNN</c> (e.g., <c>CN-2026-000008</c>). Immutable post-allocation (I-CN-3).
/// </summary>
public sealed partial record CreditNoteNumber : ValueObject
{
    private const string Pattern = @"^CN-\d{4}-\d{6}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FormatRegex();

    public string Value { get; private init; } = null!;

    private CreditNoteNumber()
    {
    }

    public static Result<CreditNoteNumber> Create(int year, long sequence)
    {
        if (year is < 1900 or > 9999)
        {
            return Result.Fail<CreditNoteNumber>(new ValidationError(
                nameof(year), "Year must be 1900-9999.", "Invoicing.InvalidCreditNoteNumberYear"));
        }

        if (sequence is < 1 or > 999999)
        {
            return Result.Fail<CreditNoteNumber>(new ValidationError(
                nameof(sequence), "Sequence must be in [1, 999999].", "Invoicing.InvalidCreditNoteNumberSequence"));
        }

        var formatted = string.Create(
            CultureInfo.InvariantCulture,
            $"CN-{year:D4}-{sequence:D6}");
        return Result.Ok(new CreditNoteNumber { Value = formatted });
    }

    public static Result<CreditNoteNumber> FromRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !FormatRegex().IsMatch(raw))
        {
            return Result.Fail<CreditNoteNumber>(new ValidationError(
                nameof(raw), "CreditNoteNumber must match format CN-YYYY-NNNNNN.", "Invoicing.InvalidCreditNoteNumberFormat"));
        }

        return Result.Ok(new CreditNoteNumber { Value = raw });
    }

    public int Year => int.Parse(Value.AsSpan(3, 4), CultureInfo.InvariantCulture);

    public long Sequence => long.Parse(Value.AsSpan(8, 6), CultureInfo.InvariantCulture);

    public override string ToString() => Value;
}
