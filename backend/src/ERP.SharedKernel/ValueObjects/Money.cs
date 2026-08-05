using System.Globalization;

namespace ERP.SharedKernel.ValueObjects;

/// <summary>
/// An amount in a specific currency.
/// </summary>
/// <remarks>
/// <para>
/// Money is never a bare <see cref="decimal"/> in this system. A naked decimal
/// carries no currency, so nothing stops adding riyals to rupees and producing a
/// number that looks plausible and is wrong. Here that combination will not
/// compile past the currency guard.
/// </para>
/// <para>
/// Backed by <see cref="decimal"/> rather than <see cref="double"/> because
/// decimal is base-10 and exact for the values money takes. In binary floating
/// point <c>0.1 + 0.2 != 0.3</c>, and a trial balance that fails to balance by
/// a fraction of a fils is not something an accountant will tolerate.
/// </para>
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Gets the amount. Negative values are legitimate - a credit, a refund, a loss.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the currency.</summary>
    public CurrencyCode Currency { get; }

    /// <summary>Gets a value indicating whether the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Gets a value indicating whether the amount is greater than zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Gets a value indicating whether the amount is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Creates an amount, rounded to the currency's own scale.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The currency.</param>
    /// <returns>The rounded amount.</returns>
    public static Money Of(decimal amount, CurrencyCode currency) =>
        new(Round(amount, currency), currency);

    /// <summary>
    /// Creates an amount without rounding, preserving every supplied decimal
    /// place.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The currency.</param>
    /// <returns>The unrounded amount.</returns>
    /// <remarks>
    /// For intermediate arithmetic only - a unit rate of 0.3333 per litre, a
    /// running subtotal before tax. Rounding at every intermediate step
    /// accumulates error; round once when the figure is presented or posted.
    /// </remarks>
    public static Money Raw(decimal amount, CurrencyCode currency) => new(amount, currency);

    /// <summary>Gets zero in the given currency.</summary>
    /// <param name="currency">The currency.</param>
    /// <returns>Zero.</returns>
    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    /// <summary>Adds two amounts of the same currency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "add");
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Subtracts one amount from another of the same currency.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "subtract");
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>Negates an amount.</summary>
    /// <param name="value">The amount.</param>
    /// <returns>The negated amount.</returns>
    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    /// <summary>Scales an amount by a factor, such as a quantity or a tax rate.</summary>
    /// <param name="money">The amount.</param>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The scaled amount, unrounded.</returns>
    public static Money operator *(Money money, decimal factor) =>
        new(money.Amount * factor, money.Currency);

    /// <summary>Scales an amount by a factor.</summary>
    /// <param name="factor">The multiplier.</param>
    /// <param name="money">The amount.</param>
    /// <returns>The scaled amount, unrounded.</returns>
    public static Money operator *(decimal factor, Money money) => money * factor;

    /// <summary>Divides an amount by a divisor.</summary>
    /// <param name="money">The amount.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The quotient, unrounded.</returns>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="divisor"/> is zero.</exception>
    public static Money operator /(Money money, decimal divisor) => divisor == 0m
        ? throw new DivideByZeroException("Cannot divide a monetary amount by zero.")
        : new Money(money.Amount / divisor, money.Currency);

    /// <summary>Determines whether one amount is less than another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when less.</returns>
    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount < right.Amount;
    }

    /// <summary>Determines whether one amount is greater than another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when greater.</returns>
    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount > right.Amount;
    }

    /// <summary>Determines whether one amount is less than or equal to another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when less or equal.</returns>
    public static bool operator <=(Money left, Money right) => !(left > right);

    /// <summary>Determines whether one amount is greater than or equal to another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when greater or equal.</returns>
    public static bool operator >=(Money left, Money right) => !(left < right);

    /// <summary>Sums amounts, which must share a currency.</summary>
    /// <param name="items">The amounts to total.</param>
    /// <param name="currency">
    /// The currency to use when <paramref name="items"/> is empty, so that
    /// summing nothing still yields a well-formed zero.
    /// </param>
    /// <returns>The total.</returns>
    public static Money Sum(IEnumerable<Money> items, CurrencyCode currency)
    {
        Money total = Zero(currency);
        foreach (Money item in items)
        {
            total += item;
        }

        return total;
    }

    /// <summary>Returns this amount rounded to its currency's scale.</summary>
    /// <returns>The rounded amount.</returns>
    public Money Rounded() => new(Round(Amount, Currency), Currency);

    /// <summary>Returns the absolute value of this amount.</summary>
    /// <returns>The absolute amount.</returns>
    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>
    /// Splits this amount into <paramref name="parts"/> shares that sum back
    /// exactly to the original.
    /// </summary>
    /// <param name="parts">How many shares to produce.</param>
    /// <returns>The shares, largest first where the split is uneven.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parts"/> is not positive.
    /// </exception>
    /// <remarks>
    /// Naive division loses money: 100.00 split three ways gives 33.33 each,
    /// totalling 99.99 and leaving a stray fils that will surface later as a
    /// trial balance that does not balance. This distributes the remainder one
    /// minor unit at a time so the shares always re-sum to the original.
    /// </remarks>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parts), parts, "Allocation requires at least one part.");
        }

        long[] ratios = new long[parts];
        Array.Fill(ratios, 1L);
        return Allocate(ratios);
    }

    /// <summary>
    /// Splits this amount in proportion to <paramref name="ratios"/>, such that
    /// the shares sum exactly to the original.
    /// </summary>
    /// <param name="ratios">
    /// Relative weights - line net amounts when apportioning a document-level
    /// discount, or line taxable values when apportioning freight.
    /// </param>
    /// <returns>The shares, in the order of <paramref name="ratios"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ratios"/> is empty, contains a negative
    /// weight, or sums to zero.
    /// </exception>
    /// <remarks>
    /// Works in whole minor units so no fraction can be lost, then hands any
    /// remainder to the earliest shares one unit at a time. This is the standard
    /// approach to the penny-distribution problem and is what makes a
    /// document-level discount apportion across lines without breaking the
    /// invoice total.
    /// </remarks>
    public Money[] Allocate(IReadOnlyList<long> ratios)
    {
        if (ratios is null || ratios.Count == 0)
        {
            throw new ArgumentException("At least one ratio is required.", nameof(ratios));
        }

        long totalRatio = 0L;
        foreach (long ratio in ratios)
        {
            if (ratio < 0)
            {
                throw new ArgumentException(
                    "Allocation ratios cannot be negative.", nameof(ratios));
            }

            totalRatio += ratio;
        }

        if (totalRatio == 0)
        {
            throw new ArgumentException(
                "Allocation ratios must not all be zero.", nameof(ratios));
        }

        // Work in whole minor units: 1234.56 QAR becomes 123456.
        int places = Currency.DecimalPlaces;
        decimal scale = Pow10(places);
        long totalMinorUnits = (long)decimal.Round(Amount * scale, 0, MidpointRounding.AwayFromZero);

        long[] shares = new long[ratios.Count];
        long distributed = 0L;

        for (int i = 0; i < ratios.Count; i++)
        {
            // Integer division truncates toward zero, so each share is never
            // over-allocated and the leftover is always distributable.
            long share = totalMinorUnits * ratios[i] / totalRatio;
            shares[i] = share;
            distributed += share;
        }

        // Hand out whatever truncation left behind, one minor unit at a time,
        // cycling through the shares so the distribution stays as even as the
        // arithmetic allows. The remainder is always smaller than the number of
        // shares, so this completes in at most one pass.
        long remainder = totalMinorUnits - distributed;
        int step = remainder >= 0 ? 1 : -1;
        int index = 0;

        while (remainder != 0)
        {
            shares[index] += step;
            remainder -= step;
            index = (index + 1) % shares.Length;
        }

        Money[] result = new Money[shares.Length];
        for (int i = 0; i < shares.Length; i++)
        {
            result[i] = new Money(shares[i] / scale, Currency);
        }

        return result;
    }

    /// <inheritdoc />
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other, "compare");
        return Amount.CompareTo(other.Amount);
    }

    /// <summary>Formats the amount for the given culture.</summary>
    /// <param name="culture">The culture, or <see langword="null"/> for the current one.</param>
    /// <returns>The formatted amount, for example <c>QAR 1,234.56</c>.</returns>
    public string ToString(CultureInfo? culture) => string.Create(
        culture ?? CultureInfo.CurrentCulture,
        $"{Currency.Code} {Amount.ToString($"N{Currency.DecimalPlaces}", culture ?? CultureInfo.CurrentCulture)}");

    /// <inheritdoc />
    public override string ToString() => ToString(CultureInfo.InvariantCulture);

    private static decimal Round(decimal amount, CurrencyCode currency) =>
        decimal.Round(amount, currency.DecimalPlaces, MidpointRounding.AwayFromZero);

    private static decimal Pow10(int exponent) => exponent switch
    {
        0 => 1m,
        1 => 10m,
        2 => 100m,
        3 => 1_000m,
        4 => 10_000m,
        _ => (decimal)Math.Pow(10, exponent),
    };

    private static void EnsureSameCurrency(Money left, Money right, string operation)
    {
        if (left.Currency != right.Currency)
        {
            // A programming error, not a user error: the domain is expected to
            // convert through an exchange rate before combining currencies.
            // Failing loudly here beats producing a meaningless total.
            throw new InvalidOperationException(
                $"Cannot {operation} amounts in different currencies " +
                $"({left.Currency} and {right.Currency}). Convert through an " +
                $"exchange rate first.");
        }
    }
}
