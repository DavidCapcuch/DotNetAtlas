using FluentValidation;

namespace Catalog.Application.Common.Validation;

/// <summary>
/// FluentValidation extensions whose semantics differ from the built-ins on Unicode-heavy input.
/// <see cref="MaximumRuneLength"/> counts Unicode scalars (runes) rather than UTF-16 code units
/// so emoji and other surrogate-pair characters are not double-counted — addresses CAT-SEC-006
/// (Wave-1 closeout) where a user input limited to N UTF-16 code units could be truncated
/// mid-surrogate, producing a malformed string or an unexpected byte count downstream.
/// </summary>
internal static class ValidationExtensions
{
    public static IRuleBuilderOptions<T, string?> MaximumRuneLength<T>(
        this IRuleBuilder<T, string?> ruleBuilder, int max)
    {
        return ruleBuilder
            .Must(value => value is null || RuneCount(value) <= max)
            .WithMessage($"The length of '{{PropertyName}}' must be {max} characters or fewer (counting Unicode runes).");
    }

    private static int RuneCount(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
