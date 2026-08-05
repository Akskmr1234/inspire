using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.SharedKernel;

/// <summary>
/// Tests for <see cref="Money"/>.
/// </summary>
/// <remarks>
/// The allocation tests carry the most weight here. Apportioning a
/// document-level discount, freight charge, or tax across invoice lines is where
/// a rounding bug silently unbalances a document, and the resulting fils
/// discrepancy surfaces days later in a trial balance rather than at the point of
/// the mistake.
/// </remarks>
public sealed class MoneyTests
{
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly CurrencyCode Kwd = CurrencyCode.FromTrusted("KWD");
    private static readonly CurrencyCode Jpy = CurrencyCode.FromTrusted("JPY");

    // ---------------------------------------------------------------- currency scale

    [Fact]
    public void Currency_scale_defaults_to_two_places()
    {
        Qar.DecimalPlaces.ShouldBe(2);
        CurrencyCode.Inr.DecimalPlaces.ShouldBe(2);
        CurrencyCode.Usd.DecimalPlaces.ShouldBe(2);
    }

    [Theory]
    [InlineData("KWD", 3)]  // Kuwaiti dinar
    [InlineData("BHD", 3)]  // Bahraini dinar
    [InlineData("OMR", 3)]  // Omani rial
    [InlineData("JPY", 0)]  // Japanese yen has no minor unit
    [InlineData("QAR", 2)]
    public void Gulf_and_zero_decimal_currencies_use_their_own_scale(string code, int expected) =>
        CurrencyCode.FromTrusted(code).DecimalPlaces.ShouldBe(expected);

    [Fact]
    public void Of_rounds_to_the_currency_scale()
    {
        Money.Of(10.005m, Qar).Amount.ShouldBe(10.01m);   // 2 places, away from zero
        Money.Of(10.0005m, Kwd).Amount.ShouldBe(10.001m); // 3 places
        Money.Of(10.5m, Jpy).Amount.ShouldBe(11m);        // 0 places
    }

    [Fact]
    public void Raw_preserves_precision_for_intermediate_arithmetic()
    {
        // A unit rate of 0.3333 must survive until the line total is computed;
        // rounding it to 0.33 first would drift on large quantities.
        Money.Raw(0.3333m, Qar).Amount.ShouldBe(0.3333m);
    }

    // ---------------------------------------------------------------- arithmetic

    [Fact]
    public void Addition_and_subtraction_operate_within_a_currency()
    {
        (Money.Of(100m, Qar) + Money.Of(25.50m, Qar)).Amount.ShouldBe(125.50m);
        (Money.Of(100m, Qar) - Money.Of(25.50m, Qar)).Amount.ShouldBe(74.50m);
    }

    [Fact]
    public void Mixing_currencies_throws_rather_than_producing_a_meaningless_total()
    {
        Money riyals = Money.Of(100m, Qar);
        Money rupees = Money.Of(100m, CurrencyCode.Inr);

        // This is a programming error: the domain is expected to convert through
        // an exchange rate first. Failing loudly beats a plausible wrong number.
        Should.Throw<InvalidOperationException>(() => riyals + rupees);
        Should.Throw<InvalidOperationException>(() => riyals - rupees);
        Should.Throw<InvalidOperationException>(() => riyals < rupees);
    }

    [Fact]
    public void Decimal_backing_avoids_binary_floating_point_drift()
    {
        // The canonical float failure: 0.1 + 0.2 != 0.3 in binary floating point.
        Money tenth = Money.Of(0.10m, Qar);
        Money fifth = Money.Of(0.20m, Qar);

        (tenth + fifth).ShouldBe(Money.Of(0.30m, Qar));
    }

    [Fact]
    public void Sum_of_an_empty_sequence_is_a_well_formed_zero()
    {
        Money total = Money.Sum([], Qar);

        total.IsZero.ShouldBeTrue();
        total.Currency.ShouldBe(Qar);
    }

    [Fact]
    public void Division_by_zero_throws()
    {
        Should.Throw<DivideByZeroException>(() => Money.Of(100m, Qar) / 0m);
    }

    // ---------------------------------------------------------------- allocation

    [Fact]
    public void Allocating_an_indivisible_amount_loses_nothing()
    {
        // The textbook case: naive division gives 33.33 x 3 = 99.99, losing a fils.
        Money[] shares = Money.Of(100m, Qar).Allocate(3);

        shares.Length.ShouldBe(3);
        shares[0].Amount.ShouldBe(33.34m);
        shares[1].Amount.ShouldBe(33.33m);
        shares[2].Amount.ShouldBe(33.33m);
        Money.Sum(shares, Qar).ShouldBe(Money.Of(100m, Qar));
    }

    [Fact]
    public void Allocating_by_weight_matches_the_weights_and_still_sums_exactly()
    {
        // A 10.00 discount across lines whose net values are 30 / 20 / 50.
        Money[] shares = Money.Of(10m, Qar).Allocate([30, 20, 50]);

        shares[0].Amount.ShouldBe(3.00m);
        shares[1].Amount.ShouldBe(2.00m);
        shares[2].Amount.ShouldBe(5.00m);
        Money.Sum(shares, Qar).ShouldBe(Money.Of(10m, Qar));
    }

    [Fact]
    public void Allocating_an_awkward_weighting_still_sums_exactly()
    {
        Money[] shares = Money.Of(0.05m, Qar).Allocate([3, 7]);

        // 5 fils split 30/70 cannot divide cleanly; the remainder is handed out
        // rather than dropped.
        Money.Sum(shares, Qar).ShouldBe(Money.Of(0.05m, Qar));
        shares.ShouldAllBe(s => s.Amount >= 0m);
    }

    [Fact]
    public void Allocation_respects_a_three_decimal_currency()
    {
        Money[] shares = Money.Of(1m, Kwd).Allocate(3);

        Money.Sum(shares, Kwd).ShouldBe(Money.Of(1m, Kwd));
        shares[0].Amount.ShouldBe(0.334m);
        shares[1].Amount.ShouldBe(0.333m);
        shares[2].Amount.ShouldBe(0.333m);
    }

    [Fact]
    public void Allocation_respects_a_zero_decimal_currency()
    {
        Money[] shares = Money.Of(10m, Jpy).Allocate(3);

        Money.Sum(shares, Jpy).ShouldBe(Money.Of(10m, Jpy));
        shares.Select(s => s.Amount).ShouldBe([4m, 3m, 3m]);
    }

    [Fact]
    public void Allocating_a_negative_amount_preserves_the_total()
    {
        // Credit notes and reversals allocate too, and must not gain a fils.
        Money[] shares = Money.Of(-100m, Qar).Allocate(3);

        Money.Sum(shares, Qar).ShouldBe(Money.Of(-100m, Qar));
        shares.ShouldAllBe(s => s.Amount < 0m);
    }

    [Fact]
    public void Allocating_zero_yields_zero_shares_that_sum_to_zero()
    {
        Money[] shares = Money.Zero(Qar).Allocate([1, 1, 1]);

        shares.ShouldAllBe(s => s.IsZero);
        Money.Sum(shares, Qar).IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Zero_weights_receive_nothing_while_the_total_is_preserved()
    {
        Money[] shares = Money.Of(10m, Qar).Allocate([0, 1, 0]);

        shares[1].Amount.ShouldBe(10m);
        Money.Sum(shares, Qar).ShouldBe(Money.Of(10m, Qar));
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(1, 7)]
    [InlineData(0.01, 5)]
    [InlineData(99999.99, 13)]
    [InlineData(12345.67, 100)]
    public void Allocation_always_sums_back_to_the_original(decimal amount, int parts)
    {
        Money original = Money.Of(amount, Qar);

        Money.Sum(original.Allocate(parts), Qar).ShouldBe(original);
    }

    [Fact]
    public void Allocation_rejects_invalid_inputs()
    {
        Money amount = Money.Of(100m, Qar);

        Should.Throw<ArgumentOutOfRangeException>(() => amount.Allocate(0));
        Should.Throw<ArgumentOutOfRangeException>(() => amount.Allocate(-1));
        Should.Throw<ArgumentException>(() => amount.Allocate([]));
        Should.Throw<ArgumentException>(() => amount.Allocate([1, -1]));
        Should.Throw<ArgumentException>(() => amount.Allocate([0, 0]));
    }

    // ---------------------------------------------------------------- parsing

    [Theory]
    [InlineData("qar", "QAR")]
    [InlineData(" inr ", "INR")]
    [InlineData("UsD", "USD")]
    public void Currency_parsing_normalises_case_and_whitespace(string input, string expected) =>
        CurrencyCode.Create(input).Value.Code.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("QA")]
    [InlineData("QARS")]
    [InlineData("Q4R")]
    public void Currency_parsing_rejects_malformed_codes(string? input) =>
        CurrencyCode.Create(input).IsFailure.ShouldBeTrue();
}
