namespace ERP.Domain.Taxation;

/// <summary>
/// The tax conditions in force for a document, assembled from the firm's regime,
/// the document's mode, the relevant reverse-tax setting, and the place of supply.
/// </summary>
/// <param name="Regime">The firm's statutory tax system.</param>
/// <param name="Mode">How this particular document treats tax.</param>
/// <param name="AmountsIncludeTax">
/// Whether entered amounts are tax-inclusive. This is the specification's
/// "Reverse Tax Calculation" setting, which is configured separately for sales and
/// for purchase - so the caller passes whichever applies to the document at hand.
/// </param>
/// <param name="IsInterStateSupply">
/// Whether the supply crosses a state boundary, determining IGST versus
/// CGST + SGST. Derived by comparing the customer's state with the firm's.
/// Meaningless outside <see cref="TaxRegime.IndiaGst"/> and ignored there.
/// </param>
public readonly record struct TaxContext(
    TaxRegime Regime,
    DocumentTaxMode Mode,
    bool AmountsIncludeTax,
    bool IsInterStateSupply)
{
    /// <summary>A context in which no tax is charged.</summary>
    public static TaxContext Untaxed => new(TaxRegime.None, DocumentTaxMode.NonTax, false, false);
}
