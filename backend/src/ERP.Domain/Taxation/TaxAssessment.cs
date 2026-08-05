using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Taxation;

/// <summary>
/// The fully-resolved tax position of a line or document.
/// </summary>
/// <remarks>
/// <para>
/// Guarantees, relied upon by posting and by the tax returns:
/// </para>
/// <list type="bullet">
/// <item><description><c>TaxableAmount + TotalTax == GrossAmount</c>, exactly.</description></item>
/// <item><description>The component amounts sum to <c>TotalTax</c>, exactly.</description></item>
/// </list>
/// <para>
/// Both hold to the last minor unit, which is why the components are derived by
/// allocation from the computed total rather than each being calculated
/// independently from the base.
/// </para>
/// </remarks>
public sealed record TaxAssessment
{
    private TaxAssessment(
        Money taxableAmount,
        Money totalTax,
        Money grossAmount,
        IReadOnlyList<TaxComponentAmount> components)
    {
        TaxableAmount = taxableAmount;
        TotalTax = totalTax;
        GrossAmount = grossAmount;
        Components = components;
    }

    /// <summary>Gets the value on which tax is charged, exclusive of tax.</summary>
    public Money TaxableAmount { get; }

    /// <summary>Gets the total tax across every component.</summary>
    public Money TotalTax { get; }

    /// <summary>Gets the tax-inclusive total.</summary>
    public Money GrossAmount { get; }

    /// <summary>Gets the per-head breakdown. Empty when no tax applies.</summary>
    public IReadOnlyList<TaxComponentAmount> Components { get; }

    /// <summary>Gets a value indicating whether any tax was charged.</summary>
    public bool HasTax => !TotalTax.IsZero;

    /// <summary>Creates an assessment where no tax applies.</summary>
    /// <param name="amount">The line amount, which is both taxable and gross.</param>
    /// <returns>A tax-free assessment.</returns>
    internal static TaxAssessment Untaxed(Money amount)
    {
        Money rounded = amount.Rounded();
        return new TaxAssessment(rounded, Money.Zero(rounded.Currency), rounded, []);
    }

    /// <summary>Creates a taxed assessment.</summary>
    /// <param name="taxableAmount">The value tax is charged on.</param>
    /// <param name="totalTax">The total tax.</param>
    /// <param name="grossAmount">The tax-inclusive total.</param>
    /// <param name="components">The per-head breakdown.</param>
    /// <returns>The assessment.</returns>
    internal static TaxAssessment Taxed(
        Money taxableAmount,
        Money totalTax,
        Money grossAmount,
        IReadOnlyList<TaxComponentAmount> components) =>
        new(taxableAmount, totalTax, grossAmount, components);

    /// <summary>Gets the amount charged against a single head, or zero if absent.</summary>
    /// <param name="type">The tax head.</param>
    /// <returns>The amount for that head.</returns>
    public Money AmountFor(TaxComponentType type)
    {
        foreach (TaxComponentAmount component in Components)
        {
            if (component.Type == type)
            {
                return component.Amount;
            }
        }

        return Money.Zero(TaxableAmount.Currency);
    }

    /// <summary>Compares two assessments by value, including their components.</summary>
    /// <param name="other">The assessment to compare against.</param>
    /// <returns><see langword="true"/> when both describe the same tax position.</returns>
    /// <remarks>
    /// Hand-written because the compiler-generated record equality would compare
    /// <see cref="Components"/> by reference. Two assessments computed separately
    /// always hold distinct list instances, so the generated version reports
    /// unequal for identical tax positions - a trap that would surface as a
    /// baffling test failure or a cache that never hits.
    /// </remarks>
    public bool Equals(TaxAssessment? other) =>
        other is not null
        && TaxableAmount == other.TaxableAmount
        && TotalTax == other.TotalTax
        && GrossAmount == other.GrossAmount
        && Components.SequenceEqual(other.Components);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(TaxableAmount);
        hash.Add(TotalTax);
        hash.Add(GrossAmount);

        foreach (TaxComponentAmount component in Components)
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One tax head's contribution to a line.</summary>
/// <param name="Type">Which tax head this is.</param>
/// <param name="Rate">
/// The rate attributable to this head. For an intra-state GST supply this is the
/// half-rate, so an 18% supply reports 9% against CGST and 9% against SGST -
/// which is what must appear on the printed invoice.
/// </param>
/// <param name="Amount">The tax amount for this head.</param>
public sealed record TaxComponentAmount(TaxComponentType Type, TaxRate Rate, Money Amount);
