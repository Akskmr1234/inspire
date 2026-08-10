using ERP.Domain.Accounting;
using ERP.Domain.Taxation;

namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>
/// A standard chart of accounts, sufficient to post the transactions the shipped
/// screens produce.
/// </summary>
/// <remarks>
/// <para>
/// A starting point, not a prescription. Every accountant reshapes the chart to
/// suit the business, so this seeds the groups and the ledgers the software itself
/// needs to reference - a cash account, a bank account, sales, purchases, and the
/// tax heads - and stops there. Seeding a full statutory chart would create dozens
/// of ledgers nobody asked for and then have to be deleted.
/// </para>
/// <para>
/// The tax ledgers depend on the firm's <see cref="TaxRegime"/>. Seeding GST heads
/// into a Qatar VAT firm would put ledgers on its trial balance that can never be
/// posted to, and would offer an accountant a choice that is wrong in their
/// jurisdiction.
/// </para>
/// </remarks>
internal static class ChartOfAccountsTemplate
{
    /// <summary>The five roots, seeded as undeletable system groups.</summary>
    internal static IReadOnlyList<GroupTemplate> Roots { get; } =
    [
        new("1000", "Assets", AccountNature.Asset, "Balance Sheet"),
        new("2000", "Liabilities", AccountNature.Liability, "Balance Sheet"),
        new("3000", "Equity", AccountNature.Equity, "Balance Sheet"),
        new("4000", "Income", AccountNature.Income, "Profit and Loss"),
        new("5000", "Expenses", AccountNature.Expense, "Profit and Loss"),
    ];

    /// <summary>The groups beneath each root, which inherit its nature.</summary>
    internal static IReadOnlyList<ChildGroupTemplate> Children { get; } =
    [
        new("1000", "1100", "Current Assets"),
        new("1000", "1110", "Cash and Bank"),
        new("1000", "1200", "Sundry Debtors"),
        new("1000", "1300", "Stock in Hand"),
        new("1000", "1400", "Fixed Assets"),
        new("1000", "1500", "Duties and Taxes (Input)"),

        new("2000", "2100", "Current Liabilities"),
        new("2000", "2200", "Sundry Creditors"),
        new("2000", "2300", "Duties and Taxes (Output)"),

        new("3000", "3100", "Capital Account"),
        new("3000", "3200", "Reserves and Surplus"),

        new("4000", "4100", "Sales Accounts"),
        new("4000", "4200", "Service Income"),
        new("4000", "4900", "Indirect Income"),

        new("5000", "5100", "Purchase Accounts"),
        new("5000", "5200", "Direct Expenses"),
        new("5000", "5900", "Indirect Expenses"),
    ];

    /// <summary>The ledgers seeded regardless of tax regime.</summary>
    internal static IReadOnlyList<LedgerTemplate> CommonLedgers { get; } =
    [
        new("1110", "CASH", "Cash in Hand", LedgerKind.Cash),
        new("1110", "BANK", "Bank Account", LedgerKind.Bank),
        new("1300", "STOCK", "Stock in Hand", LedgerKind.General),
        new("3100", "CAPITAL", "Capital Account", LedgerKind.General),
        new("4100", "SALES", "Sales Account", LedgerKind.General),
        new("4200", "SERVICE-INC", "Service Income", LedgerKind.General),
        new("5100", "PURCHASE", "Purchase Account", LedgerKind.General),

        // Referenced by the additional-ledger mapping the specification describes,
        // so they exist from the start rather than failing the first sales invoice
        // that carries a delivery charge.
        new("4900", "ROUND-OFF", "Round Off", LedgerKind.AdditionalCharge),
        new("5200", "FREIGHT", "Freight Charge", LedgerKind.AdditionalCharge),
        new("5200", "PACKING", "Packing Charge", LedgerKind.AdditionalCharge),
        new("5900", "DELIVERY", "Delivery Charge", LedgerKind.AdditionalCharge),
        new("5900", "DISC-ALLOWED", "Discount Allowed", LedgerKind.AdditionalCharge),
        new("4900", "DISC-RECEIVED", "Discount Received", LedgerKind.AdditionalCharge),

        // The counter-accounts a stock movement posts to, answering open question 8a.
        // Seeded so a new firm can post stock on its first day: the map points at these
        // and an administrator repoints it at their own chart when they have one.
        new("5200", "CONSUMPTION", "Materials Consumed", LedgerKind.General),
        new("5900", "STOCK-LOSS", "Stock Written Off", LedgerKind.General),
        new("5900", "STOCK-VARIANCE", "Stock Variance", LedgerKind.General),
        new("3100", "OPENING-STOCK", "Opening Stock Equity", LedgerKind.General),
    ];

    /// <summary>Returns the tax ledgers appropriate to a firm's regime.</summary>
    /// <param name="regime">The firm's statutory tax system.</param>
    /// <returns>The tax ledgers to seed.</returns>
    internal static IReadOnlyList<LedgerTemplate> TaxLedgersFor(TaxRegime regime) => regime switch
    {
        TaxRegime.GccVat =>
        [
            new("2300", "VAT-OUTPUT", "Output VAT", LedgerKind.Tax),
            new("1500", "VAT-INPUT", "Input VAT", LedgerKind.Tax),
        ],

        TaxRegime.IndiaGst =>
        [
            new("2300", "CGST-OUTPUT", "Output CGST", LedgerKind.Tax),
            new("2300", "SGST-OUTPUT", "Output SGST", LedgerKind.Tax),
            new("2300", "IGST-OUTPUT", "Output IGST", LedgerKind.Tax),
            new("2300", "CESS-OUTPUT", "Output Cess", LedgerKind.Tax),
            new("1500", "CGST-INPUT", "Input CGST", LedgerKind.Tax),
            new("1500", "SGST-INPUT", "Input SGST", LedgerKind.Tax),
            new("1500", "IGST-INPUT", "Input IGST", LedgerKind.Tax),
            new("1500", "CESS-INPUT", "Input Cess", LedgerKind.Tax),
        ],

        _ => [],
    };

    /// <summary>A root account group.</summary>
    /// <param name="Code">The group code.</param>
    /// <param name="Name">The group name.</param>
    /// <param name="Nature">Which side of the books it sits on.</param>
    /// <param name="Schedule">The statutory schedule it presents under.</param>
    internal sealed record GroupTemplate(
        string Code,
        string Name,
        AccountNature Nature,
        string Schedule);

    /// <summary>A group beneath another, inheriting its nature.</summary>
    /// <param name="ParentCode">The parent group's code.</param>
    /// <param name="Code">The group code.</param>
    /// <param name="Name">The group name.</param>
    internal sealed record ChildGroupTemplate(string ParentCode, string Code, string Name);

    /// <summary>A ledger to seed under a group.</summary>
    /// <param name="GroupCode">The group it reports under.</param>
    /// <param name="Code">The ledger code.</param>
    /// <param name="Name">The ledger name.</param>
    /// <param name="Kind">What the ledger represents.</param>
    internal sealed record LedgerTemplate(
        string GroupCode,
        string Code,
        string Name,
        LedgerKind Kind);
}
