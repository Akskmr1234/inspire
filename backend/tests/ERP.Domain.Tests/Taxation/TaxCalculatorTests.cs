using ERP.Domain.Taxation;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Taxation;

/// <summary>
/// Tests for <see cref="TaxCalculator"/>.
/// </summary>
/// <remarks>
/// The named worked examples come straight from sections 15.1 and 15.2 of the
/// specification. The rest exist because a tax engine fails quietly: an
/// implementation that is a fils out per line still produces a plausible-looking
/// invoice, and the error only surfaces when a VAT or GST return refuses to
/// reconcile weeks later.
/// </remarks>
public sealed class TaxCalculatorTests
{
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly CurrencyCode Inr = CurrencyCode.Inr;

    // ------------------------------------------- specification worked examples

    [Fact]
    public void Normal_tax_treats_the_entered_rate_as_exclusive()
    {
        // Specification 15.1: rate 100, GST 18% -> taxable 100, GST 18, total 118.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Inr),
            TaxRate.FromTrusted(18m),
            Gst(interState: true, inclusive: false));

        result.TaxableAmount.Amount.ShouldBe(100m);
        result.TotalTax.Amount.ShouldBe(18m);
        result.GrossAmount.Amount.ShouldBe(118m);
    }

    [Fact]
    public void Reverse_tax_treats_the_entered_rate_as_inclusive()
    {
        // Specification 15.2: entered rate 118, GST 18% -> taxable 100, GST 18,
        // total 118.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(118m, Inr),
            TaxRate.FromTrusted(18m),
            Gst(interState: true, inclusive: true));

        result.TaxableAmount.Amount.ShouldBe(100m);
        result.TotalTax.Amount.ShouldBe(18m);
        result.GrossAmount.Amount.ShouldBe(118m);
    }

    // ------------------------------------------- the core invariant

    [Theory]
    [InlineData(100, 18)]
    [InlineData(0.01, 18)]
    [InlineData(33.33, 5)]
    [InlineData(1234.56, 15)]
    [InlineData(99999.99, 7.5)]
    [InlineData(1, 3)]
    [InlineData(7.77, 12.5)]
    public void Exclusive_calculation_always_reconciles(decimal amount, decimal ratePercent)
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(amount, Qar),
            TaxRate.FromTrusted(ratePercent),
            Vat(inclusive: false));

        (result.TaxableAmount + result.TotalTax).ShouldBe(result.GrossAmount);
        Money.Sum(result.Components.Select(c => c.Amount), Qar).ShouldBe(result.TotalTax);
    }

    [Theory]
    [InlineData(118, 18)]
    [InlineData(100, 18)]
    [InlineData(0.01, 18)]
    [InlineData(33.33, 5)]
    [InlineData(1234.56, 15)]
    [InlineData(99999.99, 7.5)]
    [InlineData(1, 3)]
    [InlineData(7.77, 12.5)]
    public void Inclusive_calculation_always_reconciles(decimal gross, decimal ratePercent)
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(gross, Qar),
            TaxRate.FromTrusted(ratePercent),
            Vat(inclusive: true));

        // The gross must survive untouched - it is what the customer was quoted.
        result.GrossAmount.Amount.ShouldBe(gross);
        (result.TaxableAmount + result.TotalTax).ShouldBe(result.GrossAmount);
        Money.Sum(result.Components.Select(c => c.Amount), Qar).ShouldBe(result.TotalTax);
    }

    [Fact]
    public void Inclusive_calculation_derives_tax_by_subtraction_not_recomputation()
    {
        // 100.00 inclusive of 18% gives a taxable value of 84.745762..., rounding
        // to 84.75. Recomputing tax as 84.75 x 18% gives 15.255 -> 15.26, and
        // 84.75 + 15.26 = 100.01. Subtracting instead yields 15.25 and reconciles.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Qar),
            TaxRate.FromTrusted(18m),
            Vat(inclusive: true));

        result.TaxableAmount.Amount.ShouldBe(84.75m);
        result.TotalTax.Amount.ShouldBe(15.25m);
        result.GrossAmount.Amount.ShouldBe(100m);
    }

    // ------------------------------------------- GST component split

    [Fact]
    public void Intra_state_gst_splits_into_cgst_and_sgst_at_half_the_rate()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(1000m, Inr),
            TaxRate.FromTrusted(18m),
            Gst(interState: false, inclusive: false));

        result.Components.Count.ShouldBe(2);
        result.AmountFor(TaxComponentType.Cgst).Amount.ShouldBe(90m);
        result.AmountFor(TaxComponentType.Sgst).Amount.ShouldBe(90m);
        result.AmountFor(TaxComponentType.Igst).IsZero.ShouldBeTrue();

        // The printed invoice must show 9% against each head, not 18%.
        result.Components.ShouldAllBe(c => c.Rate.Percentage == 9m);
    }

    [Fact]
    public void An_odd_gst_amount_splits_without_gaining_or_losing_a_paisa()
    {
        // Tax of 15.25 cannot halve evenly. Applying 9% twice to the base would
        // round each half up and overstate the total.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Inr),
            TaxRate.FromTrusted(18m),
            Gst(interState: false, inclusive: true));

        Money cgst = result.AmountFor(TaxComponentType.Cgst);
        Money sgst = result.AmountFor(TaxComponentType.Sgst);

        result.TotalTax.Amount.ShouldBe(15.25m);
        (cgst + sgst).ShouldBe(result.TotalTax);
        cgst.Amount.ShouldBe(7.63m);
        sgst.Amount.ShouldBe(7.62m);
    }

    [Fact]
    public void Inter_state_gst_levies_igst_at_the_full_rate()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(1000m, Inr),
            TaxRate.FromTrusted(18m),
            Gst(interState: true, inclusive: false));

        result.Components.Count.ShouldBe(1);
        result.AmountFor(TaxComponentType.Igst).Amount.ShouldBe(180m);
        result.AmountFor(TaxComponentType.Cgst).IsZero.ShouldBeTrue();
        result.AmountFor(TaxComponentType.Sgst).IsZero.ShouldBeTrue();
        result.Components[0].Rate.Percentage.ShouldBe(18m);
    }

    // ------------------------------------------- VAT regime

    [Fact]
    public void Vat_produces_a_single_component()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(200m, Qar),
            TaxRate.FromTrusted(5m),
            Vat(inclusive: false));

        result.Components.Count.ShouldBe(1);
        result.Components[0].Type.ShouldBe(TaxComponentType.Vat);
        result.AmountFor(TaxComponentType.Vat).Amount.ShouldBe(10m);
    }

    [Fact]
    public void A_vat_firm_never_produces_gst_components()
    {
        // The two regimes coexist in one instance; a VAT firm's postings must not
        // leak GST heads into the GST return.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(200m, Qar),
            TaxRate.FromTrusted(5m),
            Vat(inclusive: false));

        result.Components.ShouldAllBe(c => c.Type == TaxComponentType.Vat);
    }

    [Fact]
    public void Place_of_supply_is_irrelevant_under_vat()
    {
        // IsInterStateSupply is an Indian GST concept and must be inert elsewhere.
        TaxAssessment across = TaxCalculator.Calculate(
            Money.Of(200m, Qar), TaxRate.FromTrusted(5m),
            new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, true));

        TaxAssessment within = TaxCalculator.Calculate(
            Money.Of(200m, Qar), TaxRate.FromTrusted(5m),
            new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false));

        across.ShouldBe(within);
    }

    [Fact]
    public void A_three_decimal_currency_reconciles()
    {
        // Bahraini dinar has three minor-unit digits, so rounding happens at a
        // different scale from the Qatari riyal.
        CurrencyCode bhd = CurrencyCode.FromTrusted("BHD");

        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, bhd),
            TaxRate.FromTrusted(10m),
            Vat(inclusive: true));

        result.GrossAmount.Amount.ShouldBe(100m);
        (result.TaxableAmount + result.TotalTax).ShouldBe(result.GrossAmount);
        result.TaxableAmount.Amount.ShouldBe(90.909m);
        result.TotalTax.Amount.ShouldBe(9.091m);
    }

    // ------------------------------------------- no-tax paths

    [Fact]
    public void Non_tax_mode_charges_nothing_even_with_a_rate_present()
    {
        // Mode NT overrides the product's tax rate entirely.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Qar),
            TaxRate.FromTrusted(18m),
            new TaxContext(TaxRegime.GccVat, DocumentTaxMode.NonTax, false, false));

        result.HasTax.ShouldBeFalse();
        result.TotalTax.IsZero.ShouldBeTrue();
        result.TaxableAmount.Amount.ShouldBe(100m);
        result.GrossAmount.Amount.ShouldBe(100m);
        result.Components.ShouldBeEmpty();
    }

    [Fact]
    public void A_zero_rate_charges_nothing()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Qar), TaxRate.Zero, Vat(inclusive: false));

        result.HasTax.ShouldBeFalse();
        result.GrossAmount.Amount.ShouldBe(100m);
        result.Components.ShouldBeEmpty();
    }

    [Fact]
    public void A_firm_with_no_regime_charges_nothing()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(100m, Qar),
            TaxRate.FromTrusted(18m),
            new TaxContext(TaxRegime.None, DocumentTaxMode.Taxable, false, false));

        result.HasTax.ShouldBeFalse();
        result.Components.ShouldBeEmpty();
    }

    [Fact]
    public void A_zero_amount_produces_zero_tax()
    {
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Zero(Qar), TaxRate.FromTrusted(18m), Vat(inclusive: false));

        result.TotalTax.IsZero.ShouldBeTrue();
        result.GrossAmount.IsZero.ShouldBeTrue();
    }

    // ------------------------------------------- CST legacy mode

    [Fact]
    public void Central_sales_tax_mode_produces_a_cst_component()
    {
        // Retained so historical documents from the source system remain
        // reproducible.
        TaxAssessment result = TaxCalculator.Calculate(
            Money.Of(1000m, Inr),
            TaxRate.FromTrusted(2m),
            new TaxContext(TaxRegime.IndiaGst, DocumentTaxMode.CentralSalesTax, false, true));

        result.Components.Count.ShouldBe(1);
        result.Components[0].Type.ShouldBe(TaxComponentType.Cst);
        result.AmountFor(TaxComponentType.Cst).Amount.ShouldBe(20m);
    }

    // ------------------------------------------- rate validation

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-18)]
    public void A_negative_rate_is_rejected(decimal percent) =>
        TaxRate.Create(percent).Error.Code.ShouldBe("TaxRate.Negative");

    [Fact]
    public void A_rate_above_one_hundred_percent_is_rejected()
    {
        // Guards the 1800-instead-of-18.00 typo, which would otherwise multiply
        // an invoice by nineteen.
        TaxRate.Create(1800m).Error.Code.ShouldBe("TaxRate.TooLarge");
    }

    [Fact]
    public void A_rate_of_exactly_one_hundred_percent_is_accepted() =>
        TaxRate.Create(100m).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(18, 9)]
    [InlineData(5, 2.5)]
    [InlineData(0, 0)]
    [InlineData(12, 6)]
    public void Halving_a_rate_gives_the_per_head_gst_rate(decimal full, decimal expectedHalf) =>
        TaxRate.FromTrusted(full).Half().Percentage.ShouldBe(expectedHalf);

    // ------------------------------------------- helpers

    private static TaxContext Vat(bool inclusive) =>
        new(TaxRegime.GccVat, DocumentTaxMode.Taxable, inclusive, false);

    private static TaxContext Gst(bool interState, bool inclusive) =>
        new(TaxRegime.IndiaGst, DocumentTaxMode.Taxable, inclusive, interState);
}
