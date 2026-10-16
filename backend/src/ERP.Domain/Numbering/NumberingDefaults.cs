namespace ERP.Domain.Numbering;

/// <summary>
/// How a document type is numbered before anybody configures it.
/// </summary>
/// <remarks>
/// <para>
/// One table rather than a literal in each handler. The prefixes used to be written
/// into the eleven places that open a series, and two of those places disagreed with
/// the rest about what a prefix meant: a purchase return and the goods receipt behind a
/// purchase were both "PR", and a sales return and the stock movement behind one were
/// both "SR", so two different documents could carry the same visible number. Here the
/// stock movements a commercial document posts carry prefixes of their own - GRN for a
/// goods received note, SRN for goods coming back from a customer - and the screens that
/// show a firm its numbering read the same table the handlers use.
/// </para>
/// <para>
/// A firm that has already issued numbers keeps its series exactly as they are: these
/// defaults only shape a series that does not exist yet.
/// </para>
/// </remarks>
public static class NumberingDefaults
{
    /// <summary>The character between a number's segments.</summary>
    public const string Separator = "/";

    /// <summary>Gets the prefix a document type starts with.</summary>
    /// <param name="documentType">A value from <see cref="DocumentTypes"/>.</param>
    /// <returns>The prefix.</returns>
    public static string PrefixFor(string documentType) => documentType switch
    {
        DocumentTypes.CashReceipt => "CR",
        DocumentTypes.BankReceipt => "BR",
        DocumentTypes.CashPayment => "CP",
        DocumentTypes.BankPayment => "BP",
        DocumentTypes.Journal => "JV",
        DocumentTypes.Contra => "CN",
        DocumentTypes.OpeningBalance => "OB",
        DocumentTypes.SalesQuotation => "SQ",
        DocumentTypes.SalesOrder => "SO",
        DocumentTypes.DeliveryNote => "DN",
        DocumentTypes.SalesInvoice => "SL",
        DocumentTypes.SalesReturn => "SR",
        DocumentTypes.PurchaseOrder => "PO",
        DocumentTypes.PurchaseInvoice => "PU",
        DocumentTypes.PurchaseReturn => "PR",
        DocumentTypes.JobCard => "JC",
        DocumentTypes.OpeningStock => "OS",
        DocumentTypes.MaterialReceipt => "MR",
        DocumentTypes.MaterialIssue => "MI",
        DocumentTypes.StockTransfer => "ST",
        DocumentTypes.StockAdjustment => "ADJ",
        DocumentTypes.DamagedStock => "DMG",
        DocumentTypes.PhysicalVerification => "PV",
        DocumentTypes.SalesIssue => "SI",
        DocumentTypes.SalesReturnReceipt => "SRN",
        DocumentTypes.PurchaseReceipt => "GRN",
        DocumentTypes.PurchaseReturnIssue => "PRN",
        _ => "DOC",
    };

    /// <summary>
    /// Shapes a newly created series the way every series starts: the prefix, the
    /// financial year, then the counter, joined by <see cref="Separator"/>.
    /// </summary>
    /// <param name="series">The series, just created.</param>
    /// <param name="financialYearLabel">The year's label, such as <c>2026</c>.</param>
    public static void Apply(NumberingSeries series, string? financialYearLabel)
    {
        ArgumentNullException.ThrowIfNull(series);

        series.SetFormat(
            prefix: PrefixFor(series.DocumentType),
            suffix: null,
            separator: Separator,
            financialYearLabel: financialYearLabel);
    }
}
