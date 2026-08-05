using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Taxation;

/// <summary>
/// Computes tax for a line or document.
/// </summary>
/// <remarks>
/// <para>
/// A pure function with no dependencies and no state, so every rule below is
/// directly testable without a database, a document, or a firm.
/// </para>
/// <para>
/// Two properties are non-negotiable and shape the implementation:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <c>taxable + tax == gross</c> exactly. In the inclusive case the tax is
/// obtained by <em>subtracting</em> the derived taxable value from the gross,
/// never by computing it independently, because two separately-rounded figures
/// need not add up.
/// </description>
/// </item>
/// <item>
/// <description>
/// Components sum to the total tax exactly. The CGST/SGST split allocates the
/// already-computed total rather than applying a half-rate to the base twice,
/// which for an odd rate on an odd base loses a minor unit.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class TaxCalculator
{
    /// <summary>Computes the tax position of an amount.</summary>
    /// <param name="amount">
    /// The line amount. Interpreted as tax-exclusive, or as tax-inclusive when
    /// <see cref="TaxContext.AmountsIncludeTax"/> is set.
    /// </param>
    /// <param name="rate">The tax rate, normally defaulted from the product master.</param>
    /// <param name="context">The tax conditions in force.</param>
    /// <returns>The resolved tax position.</returns>
    public static TaxAssessment Calculate(Money amount, TaxRate rate, TaxContext context)
    {
        bool noTaxApplies =
            context.Mode == DocumentTaxMode.NonTax
            || context.Regime == TaxRegime.None
            || rate.IsZero;

        if (noTaxApplies)
        {
            return TaxAssessment.Untaxed(amount);
        }

        (Money taxable, Money totalTax, Money gross) = context.AmountsIncludeTax
            ? SplitInclusive(amount, rate)
            : AddExclusive(amount, rate);

        IReadOnlyList<TaxComponentAmount> components = ResolveComponents(totalTax, rate, context);

        return TaxAssessment.Taxed(taxable, totalTax, gross, components);
    }

    /// <summary>
    /// Derives the taxable value and tax from a tax-inclusive amount - the
    /// specification's reverse tax calculation.
    /// </summary>
    /// <param name="grossAmount">The tax-inclusive amount as entered.</param>
    /// <param name="rate">The tax rate.</param>
    /// <returns>The taxable value, the tax, and the gross.</returns>
    /// <remarks>
    /// Worked example from the specification: an entered rate of 118 at 18% yields
    /// a taxable amount of 100, tax of 18, and a total of 118.
    /// </remarks>
    private static (Money Taxable, Money Tax, Money Gross) SplitInclusive(
        Money grossAmount,
        TaxRate rate)
    {
        Money gross = grossAmount.Rounded();

        // gross = taxable * (1 + r)  =>  taxable = gross / (1 + r)
        Money taxable = Money.Of(gross.Amount / (1m + rate.Multiplier), gross.Currency);

        // Subtracting guarantees taxable + tax == gross to the minor unit.
        // Computing tax independently as taxable * r would not.
        Money tax = gross - taxable;

        return (taxable, tax, gross);
    }

    /// <summary>Adds tax to a tax-exclusive amount.</summary>
    /// <param name="netAmount">The tax-exclusive amount as entered.</param>
    /// <param name="rate">The tax rate.</param>
    /// <returns>The taxable value, the tax, and the gross.</returns>
    /// <remarks>
    /// Worked example from the specification: an entered rate of 100 at 18% yields
    /// a taxable amount of 100, tax of 18, and a total of 118.
    /// </remarks>
    private static (Money Taxable, Money Tax, Money Gross) AddExclusive(
        Money netAmount,
        TaxRate rate)
    {
        Money taxable = netAmount.Rounded();
        Money tax = Money.Of(taxable.Amount * rate.Multiplier, taxable.Currency);

        return (taxable, tax, taxable + tax);
    }

    /// <summary>Breaks the total tax down into the heads the regime requires.</summary>
    /// <param name="totalTax">The total tax already computed.</param>
    /// <param name="rate">The full rate.</param>
    /// <param name="context">The tax conditions in force.</param>
    /// <returns>The per-head breakdown, summing exactly to <paramref name="totalTax"/>.</returns>
    private static IReadOnlyList<TaxComponentAmount> ResolveComponents(
        Money totalTax,
        TaxRate rate,
        TaxContext context)
    {
        if (context.Mode == DocumentTaxMode.CentralSalesTax)
        {
            return [new TaxComponentAmount(TaxComponentType.Cst, rate, totalTax)];
        }

        return context.Regime switch
        {
            TaxRegime.GccVat =>
                [new TaxComponentAmount(TaxComponentType.Vat, rate, totalTax)],

            // Inter-state supply attracts IGST at the full rate instead of the
            // CGST/SGST pair.
            TaxRegime.IndiaGst when context.IsInterStateSupply =>
                [new TaxComponentAmount(TaxComponentType.Igst, rate, totalTax)],

            TaxRegime.IndiaGst => SplitGstEqually(totalTax, rate),

            _ => [],
        };
    }

    /// <summary>Splits GST into its central and state halves.</summary>
    /// <param name="totalTax">The total GST.</param>
    /// <param name="rate">The full rate; each head reports half of it.</param>
    /// <returns>The CGST and SGST amounts.</returns>
    /// <remarks>
    /// Allocation rather than arithmetic: 15.25 halves to 7.625, which rounds to
    /// 7.63 twice and overstates the tax by a fils. Allocating the computed total
    /// gives 7.63 and 7.62, which re-sum correctly. Where the split is uneven the
    /// extra minor unit goes to CGST - arbitrary, but deterministic, so the same
    /// document always produces the same figures.
    /// </remarks>
    private static IReadOnlyList<TaxComponentAmount> SplitGstEqually(Money totalTax, TaxRate rate)
    {
        Money[] halves = totalTax.Allocate(2);
        TaxRate halfRate = rate.Half();

        return
        [
            new TaxComponentAmount(TaxComponentType.Cgst, halfRate, halves[0]),
            new TaxComponentAmount(TaxComponentType.Sgst, halfRate, halves[1]),
        ];
    }
}
