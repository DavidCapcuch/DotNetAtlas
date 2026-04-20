using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Platform.SharedKernel.ValueObjects;

/// <summary>
/// Postal address value object. Shared-kernel (ADR-0015 / Wave 0 pin).
/// Country code is ISO 3166-1 alpha-2.
/// </summary>
/// <param name="Street1">Primary street line (required, max 200 chars).</param>
/// <param name="Street2">Secondary street line (optional, max 200 chars).</param>
/// <param name="City">City / locality (required, max 100 chars).</param>
/// <param name="State">Region / state / province (optional, max 100 chars).</param>
/// <param name="PostalCode">Postal / ZIP code (required, max 20 chars).</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 (uppercase, 2 chars).</param>
public sealed record Address(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode) : ValueObject
{
    /// <summary>
    /// Creates an <see cref="Address"/> with validation.
    /// </summary>
    /// <param name="street1">Primary street line.</param>
    /// <param name="street2">Secondary street line.</param>
    /// <param name="city">City.</param>
    /// <param name="state">State/region.</param>
    /// <param name="postalCode">Postal code.</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2.</param>
    /// <returns>Validated result.</returns>
    public static Result<Address> Create(
        string street1,
        string? street2,
        string city,
        string? state,
        string postalCode,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(street1) || street1.Length > 200)
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(Street1), "Street1 is required and must be ≤ 200 chars.", "Address.InvalidStreet1"));
        }

        if (street2 is { Length: > 200 })
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(Street2), "Street2 must be ≤ 200 chars.", "Address.InvalidStreet2"));
        }

        if (string.IsNullOrWhiteSpace(city) || city.Length > 100)
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(City), "City is required and must be ≤ 100 chars.", "Address.InvalidCity"));
        }

        if (state is { Length: > 100 })
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(State), "State must be ≤ 100 chars.", "Address.InvalidState"));
        }

        if (string.IsNullOrWhiteSpace(postalCode) || postalCode.Length > 20)
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(PostalCode), "PostalCode is required and must be ≤ 20 chars.", "Address.InvalidPostalCode"));
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
        {
            return Result.Fail<Address>(new ValidationError(
                nameof(CountryCode),
                "CountryCode must be ISO 3166-1 alpha-2 (2 uppercase letters).",
                "Address.InvalidCountryCode"));
        }

        return Result.Ok(new Address(
            street1.Trim(),
            street2?.Trim(),
            city.Trim(),
            state?.Trim(),
            postalCode.Trim(),
            countryCode.ToUpperInvariant()));
    }
}
