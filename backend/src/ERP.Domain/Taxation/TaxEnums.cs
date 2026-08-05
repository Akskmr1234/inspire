namespace ERP.Domain.Taxation;

/// <summary>
/// The statutory tax system a firm operates under.
/// </summary>
/// <remarks>
/// A per-firm setting, not a deployment-wide constant. One platform instance
/// serves GCC VAT firms and Indian GST firms simultaneously, so nothing may
/// assume a single regime.
/// </remarks>
public enum TaxRegime
{
    /// <summary>No tax is computed. Used by firms that do not charge tax at all.</summary>
    None = 0,

    /// <summary>
    /// Gulf Co-operation Council value-added tax: a single tax component per
    /// line, reported as input and output tax.
    /// </summary>
    GccVat = 1,

    /// <summary>
    /// Indian goods and services tax. A single notional rate is split into
    /// CGST plus SGST within a state, or levied as IGST across states.
    /// </summary>
    IndiaGst = 2,
}

/// <summary>
/// How an individual document treats tax, selected per document by the
/// <c>Mode</c> field on the sales and service screens.
/// </summary>
public enum DocumentTaxMode
{
    /// <summary>
    /// Non-taxable (<c>NT</c> in the legacy application). No tax is computed
    /// regardless of the firm's regime or the product's tax rate.
    /// </summary>
    NonTax = 0,

    /// <summary>
    /// Taxable under the firm's regime. Under <see cref="TaxRegime.GccVat"/> this
    /// produces VAT; under <see cref="TaxRegime.IndiaGst"/> it produces the GST
    /// components.
    /// </summary>
    Taxable = 1,

    /// <summary>
    /// Central sales tax - a legacy Indian inter-state regime retained because
    /// the source system carries a <c>CST</c> ledger and historical documents
    /// must remain reproducible.
    /// </summary>
    CentralSalesTax = 2,
}

/// <summary>
/// A distinct tax head that is computed, stored, and reported separately.
/// </summary>
/// <remarks>
/// Every tax figure is persisted per component rather than collapsed into one
/// <c>TaxAmount</c> column. A VAT return and a GST return need different
/// breakdowns of the same posting, and reconstructing components from a total
/// after the fact is not possible.
/// </remarks>
public enum TaxComponentType
{
    /// <summary>GCC value-added tax.</summary>
    Vat = 1,

    /// <summary>Central GST - the central government's half of an intra-state supply.</summary>
    Cgst = 2,

    /// <summary>State GST - the state government's half of an intra-state supply.</summary>
    Sgst = 3,

    /// <summary>Integrated GST, levied instead of CGST and SGST on inter-state supply.</summary>
    Igst = 4,

    /// <summary>Compensation cess levied on top of GST for specified goods.</summary>
    Cess = 5,

    /// <summary>
    /// Food cess, carried as its own column on the sales grid in the source
    /// system and therefore its own component here.
    /// </summary>
    FoodCess = 6,

    /// <summary>Central sales tax, under <see cref="DocumentTaxMode.CentralSalesTax"/>.</summary>
    Cst = 7,
}
