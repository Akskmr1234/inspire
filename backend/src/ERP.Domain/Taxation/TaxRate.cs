using System.Globalization;
using ERP.SharedKernel.Results;

namespace ERP.Domain.Taxation;

/// <summary>
/// A tax percentage, such as 5 for GCC VAT at 5% or 18 for GST at 18%.
/// </summary>
/// <remarks>
/// <para>
/// Held as a percentage rather than a fraction because that is how it is entered,
/// displayed, printed on an invoice, and quoted in legislation. Converting to a
/// fraction at the point of arithmetic keeps a single, obvious conversion instead
/// of an ambiguous <c>0.18</c> that a reader has to work out the meaning of.
/// </para>
/// <para>
/// Four decimal places are permitted. Rates are usually whole numbers, but
/// fractional rates exist and a rate is also used to derive a taxable value from
/// an inclusive amount, where precision matters.
/// </para>
/// </remarks>
public readonly record struct TaxRate : IComparable<TaxRate>
{
    /// <summary>The maximum accepted percentage.</summary>
    /// <remarks>
    /// A cap of 100% is not a statutory limit but a typo guard: entering 1800
    /// instead of 18.00 would otherwise silently multiply an invoice by nineteen.
    /// No real rate approaches this.
    /// </remarks>
    private const decimal MaximumPercentage = 100m;

    private const int Scale = 4;

    private TaxRate(decimal percentage) => Percentage = percentage;

    /// <summary>Gets a zero rate.</summary>
    public static TaxRate Zero => new(0m);

    /// <summary>Gets the rate as a percentage, for example <c>18.0000</c>.</summary>
    public decimal Percentage { get; }

    /// <summary>
    /// Gets the rate as a multiplier, for example <c>0.18</c>. Used for
    /// arithmetic, never for display.
    /// </summary>
    public decimal Multiplier => Percentage / 100m;

    /// <summary>Gets a value indicating whether the rate is zero.</summary>
    public bool IsZero => Percentage == 0m;

    /// <summary>Compares two rates.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the left rate is lower.</returns>
    public static bool operator <(TaxRate left, TaxRate right) =>
        left.Percentage < right.Percentage;

    /// <summary>Compares two rates.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the left rate is higher.</returns>
    public static bool operator >(TaxRate left, TaxRate right) =>
        left.Percentage > right.Percentage;

    /// <summary>Compares two rates.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the left rate is not higher.</returns>
    public static bool operator <=(TaxRate left, TaxRate right) => !(left > right);

    /// <summary>Compares two rates.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the left rate is not lower.</returns>
    public static bool operator >=(TaxRate left, TaxRate right) => !(left < right);

    /// <summary>Creates a validated tax rate from a percentage.</summary>
    /// <param name="percentage">The percentage, for example <c>18</c> for 18%.</param>
    /// <returns>The rate, or a validation failure.</returns>
    public static Result<TaxRate> Create(decimal percentage)
    {
        if (percentage < 0m)
        {
            return Result.Failure<TaxRate>(Error.Validation(
                "TaxRate.Negative",
                $"A tax rate cannot be negative, but {percentage} was supplied."));
        }

        if (percentage > MaximumPercentage)
        {
            return Result.Failure<TaxRate>(Error.Validation(
                "TaxRate.TooLarge",
                $"A tax rate cannot exceed {MaximumPercentage}%, but {percentage} was " +
                $"supplied. Check whether a percentage was entered as a whole number " +
                $"by mistake."));
        }

        return Result.Success(new TaxRate(decimal.Round(percentage, Scale)));
    }

    /// <summary>Wraps a percentage already known to be valid, such as one read from the database.</summary>
    /// <param name="percentage">The validated percentage.</param>
    /// <returns>The rate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
    public static TaxRate FromTrusted(decimal percentage) =>
        percentage is >= 0m and <= MaximumPercentage
            ? new TaxRate(decimal.Round(percentage, Scale))
            : throw new ArgumentOutOfRangeException(
                nameof(percentage), percentage, "Tax rate must be between 0 and 100%.");

    /// <summary>
    /// Splits this rate into equal halves, as Indian GST requires when a supply is
    /// intra-state: 18% becomes 9% CGST plus 9% SGST.
    /// </summary>
    /// <returns>Half of this rate.</returns>
    /// <remarks>
    /// Halving the <em>rate</em> is not how the tax amount is derived - see
    /// <see cref="TaxCalculator"/>, which splits the computed tax amount instead.
    /// An odd rate such as 5% halves to 2.5%, and computing each half separately
    /// from the base can round to a pair that does not re-sum to the whole.
    /// This exists so the correct half-rate can be shown and printed per component.
    /// </remarks>
    public TaxRate Half() => new(decimal.Round(Percentage / 2m, Scale));

    /// <inheritdoc />
    public int CompareTo(TaxRate other) => Percentage.CompareTo(other.Percentage);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Percentage.Normalize()}%");
}

/// <summary>Internal decimal helpers.</summary>
internal static class DecimalExtensions
{
    /// <summary>
    /// Strips trailing zeros so a rate displays as <c>18%</c> rather than
    /// <c>18.0000%</c>.
    /// </summary>
    /// <param name="value">The value to normalise.</param>
    /// <returns>The value without trailing zeros.</returns>
    internal static decimal Normalize(this decimal value) => value / 1.000000000000000000000000000000000m;
}
