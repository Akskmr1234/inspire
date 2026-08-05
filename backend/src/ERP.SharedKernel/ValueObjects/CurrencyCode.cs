using System.Diagnostics.CodeAnalysis;
using ERP.SharedKernel.Results;

namespace ERP.SharedKernel.ValueObjects;

/// <summary>
/// An ISO 4217 three-letter currency code, together with the number of decimal
/// places that currency actually uses.
/// </summary>
/// <remarks>
/// <para>
/// Minor units are not universally two. The Gulf states this product targets
/// include several three-decimal currencies - Kuwaiti dinar, Bahraini dinar,
/// Omani rial - while the Japanese yen has none. Hardcoding two decimals would
/// silently misstate totals in those markets, so the scale travels with the
/// currency.
/// </para>
/// <para>
/// Stored as a fixed-length <c>char(3)</c>. The decimal count is resolved from
/// the table below rather than persisted per row, so a correction to the table
/// applies everywhere at once.
/// </para>
/// </remarks>
public readonly record struct CurrencyCode
{
    /// <summary>Qatari riyal - the currency of the reference deployment.</summary>
    public static readonly CurrencyCode Qar = new("QAR");

    /// <summary>Indian rupee.</summary>
    public static readonly CurrencyCode Inr = new("INR");

    /// <summary>United States dollar.</summary>
    public static readonly CurrencyCode Usd = new("USD");

    /// <summary>
    /// Currencies whose minor unit is not two decimal places. Anything absent
    /// from this table uses two, which covers the overwhelming majority.
    /// </summary>
    private static readonly Dictionary<string, int> NonStandardScales = new(StringComparer.Ordinal)
    {
        // Three decimal places.
        ["BHD"] = 3, // Bahraini dinar
        ["IQD"] = 3, // Iraqi dinar
        ["JOD"] = 3, // Jordanian dinar
        ["KWD"] = 3, // Kuwaiti dinar
        ["LYD"] = 3, // Libyan dinar
        ["OMR"] = 3, // Omani rial
        ["TND"] = 3, // Tunisian dinar

        // No minor unit.
        ["BIF"] = 0,
        ["CLP"] = 0,
        ["DJF"] = 0,
        ["GNF"] = 0,
        ["ISK"] = 0,
        ["JPY"] = 0,
        ["KMF"] = 0,
        ["KRW"] = 0,
        ["PYG"] = 0,
        ["RWF"] = 0,
        ["UGX"] = 0,
        ["VND"] = 0,
        ["VUV"] = 0,
        ["XAF"] = 0,
        ["XOF"] = 0,
        ["XPF"] = 0,

        // Four decimal places.
        ["CLF"] = 4,
        ["UYW"] = 4,
    };

    private CurrencyCode(string code) => Code = code;

    /// <summary>Gets the upper-case ISO 4217 alpha-3 code.</summary>
    public string Code { get; }

    /// <summary>
    /// Gets the number of decimal places this currency uses. All monetary
    /// rounding in the system is performed to this scale.
    /// </summary>
    public int DecimalPlaces => Code is not null && NonStandardScales.TryGetValue(Code, out int places)
        ? places
        : 2;

    /// <summary>
    /// Gets a value indicating whether this instance holds an actual currency
    /// rather than the uninitialised <see langword="default"/>.
    /// </summary>
    public bool IsSpecified => !string.IsNullOrEmpty(Code);

    /// <summary>Returns the code, so string interpolation reads naturally.</summary>
    /// <param name="currency">The currency.</param>
    public static implicit operator string(CurrencyCode currency) => currency.Code;

    /// <summary>
    /// Parses and validates a currency code, upper-casing it on the way through.
    /// </summary>
    /// <param name="code">A three-letter ISO 4217 code, in any casing.</param>
    /// <returns>
    /// The currency, or a validation failure when the input is absent or not
    /// three letters.
    /// </returns>
    public static Result<CurrencyCode> Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<CurrencyCode>(
                Error.Validation("Currency.Required", "A currency code is required."));
        }

        string trimmed = code.Trim().ToUpperInvariant();

        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<CurrencyCode>(Error.Validation(
                "Currency.Invalid",
                $"'{code}' is not a three-letter ISO 4217 currency code."));
        }

        return Result.Success(new CurrencyCode(trimmed));
    }

    /// <summary>
    /// Wraps a code already known to be valid, such as one read from the
    /// database.
    /// </summary>
    /// <param name="code">A validated three-letter code.</param>
    /// <returns>The currency.</returns>
    /// <exception cref="ArgumentException">Thrown when the code is not three letters.</exception>
    public static CurrencyCode FromTrusted(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
        {
            throw new ArgumentException(
                $"'{code}' is not a valid ISO 4217 currency code.", nameof(code));
        }

        return new CurrencyCode(code.ToUpperInvariant());
    }

    /// <summary>Attempts to parse a currency code.</summary>
    /// <param name="code">The candidate code.</param>
    /// <param name="currency">The parsed currency when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeded.</returns>
    public static bool TryParse(string? code, [NotNullWhen(true)] out CurrencyCode currency)
    {
        Result<CurrencyCode> result = Create(code);
        currency = result.IsSuccess ? result.Value : default;
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public override string ToString() => Code ?? string.Empty;
}
