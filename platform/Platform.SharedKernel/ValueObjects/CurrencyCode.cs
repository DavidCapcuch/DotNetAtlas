using Ardalis.SmartEnum;

namespace Platform.SharedKernel.ValueObjects;

/// <summary>
/// ISO 4217 currency code. Closed, curated set for the reference solution
/// (most-traded and EU-relevant codes). BCs that need additional codes
/// subclass or extend this list in a follow-up PR.
/// </summary>
/// <remarks>
/// The SmartEnum name is the three-letter code itself (e.g., <c>Usd.Name == "USD"</c>);
/// <see cref="SmartEnum{T,TValue}.Value"/> is the ISO 4217 numeric code.
/// </remarks>
public sealed class CurrencyCode : SmartEnum<CurrencyCode>
{
    public static readonly CurrencyCode Usd = new("USD", 840);
    public static readonly CurrencyCode Eur = new("EUR", 978);
    public static readonly CurrencyCode Gbp = new("GBP", 826);
    public static readonly CurrencyCode Chf = new("CHF", 756);
    public static readonly CurrencyCode Jpy = new("JPY", 392);
    public static readonly CurrencyCode Czk = new("CZK", 203);
    public static readonly CurrencyCode Pln = new("PLN", 985);
    public static readonly CurrencyCode Cad = new("CAD", 124);
    public static readonly CurrencyCode Aud = new("AUD", 36);
    public static readonly CurrencyCode Sek = new("SEK", 752);
    public static readonly CurrencyCode Nok = new("NOK", 578);
    public static readonly CurrencyCode Dkk = new("DKK", 208);
    public static readonly CurrencyCode Cny = new("CNY", 156);

    private CurrencyCode(string name, int value)
        : base(name, value)
    {
    }
}
