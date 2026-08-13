using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Purchase;

/// <summary>Reads one purchase, with its lines, its charges, and what posting it produced.</summary>
/// <param name="PurchaseInvoiceId">The document.</param>
public sealed record GetPurchaseInvoiceQuery(Guid PurchaseInvoiceId)
    : IQuery<PurchaseInvoiceDetail>;

/// <summary>One tax head as it was charged on a line.</summary>
/// <param name="Component">The head: VAT, CGST, SGST, IGST, cess.</param>
/// <param name="Percentage">The rate it was charged at.</param>
/// <param name="Amount">What that came to.</param>
public sealed record PurchaseInvoiceLineTaxDetail(
    TaxComponentType Component,
    decimal Percentage,
    decimal Amount);

/// <summary>One line of a purchase.</summary>
/// <param name="LineNumber">Its position, from one.</param>
/// <param name="ProductId">The product bought.</param>
/// <param name="BatchNumber">The batch it arrived in, where the product is batched.</param>
/// <param name="ExpiresOn">When that batch expires, where the supplier stated it.</param>
/// <param name="UnitId">The unit the quantity was entered in.</param>
/// <param name="Quantity">How many, in that unit.</param>
/// <param name="StockQuantity">The same, in the product's stock unit.</param>
/// <param name="Rate">What one cost.</param>
/// <param name="Discount">What came off before tax.</param>
/// <param name="Taxable">What the line comes to before tax.</param>
/// <param name="Tax">The input tax on it.</param>
/// <param name="Components">That tax, head by head, as the engine assessed it.</param>
/// <param name="SerialNumbers">The units arriving, where the product is serialised.</param>
public sealed record PurchaseInvoiceLineDetail(
    int LineNumber,
    Guid ProductId,
    string? BatchNumber,
    DateOnly? ExpiresOn,
    Guid UnitId,
    decimal Quantity,
    decimal StockQuantity,
    decimal Rate,
    decimal Discount,
    decimal Taxable,
    decimal Tax,
    IReadOnlyList<PurchaseInvoiceLineTaxDetail> Components,
    IReadOnlyList<string> SerialNumbers);

/// <summary>One charge carried beside the goods.</summary>
/// <param name="LedgerId">The account it posts to.</param>
/// <param name="Amount">What it comes to, always positive.</param>
/// <param name="IsAddition">Whether it adds to the total rather than deducting.</param>
public sealed record PurchaseInvoiceChargeDetail(
    Guid LedgerId,
    decimal Amount,
    bool IsAddition);

/// <summary>A purchase in full.</summary>
/// <param name="Header">Its number, status and figures.</param>
/// <param name="Date">The date the firm booked it on.</param>
/// <param name="SupplierLedgerId">The supplier billing.</param>
/// <param name="WarehouseId">The warehouse the goods arrive at.</param>
/// <param name="Mode">The tax mode it was entered under.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="SupplierInvoiceNumber">The number on the supplier's own invoice.</param>
/// <param name="SupplierInvoiceDate">The date on it.</param>
/// <param name="Narration">What is recorded against it.</param>
/// <param name="Kind">Whether goods arrived or went back.</param>
/// <param name="ReturnsInvoiceId">The purchase a return is against, where it names one.</param>
/// <param name="Lines">What was bought.</param>
/// <param name="Charges">What was carried beside it.</param>
/// <param name="StockDocumentId">The receipt that put the goods on the shelf, once posted.</param>
/// <param name="BillId">The bill owed to the supplier, once posted.</param>
/// <param name="JournalVoucherId">The journal in the nominal ledger, once posted.</param>
public sealed record PurchaseInvoiceDetail(
    PurchaseInvoiceResponse Header,
    DateOnly Date,
    Guid SupplierLedgerId,
    Guid WarehouseId,
    TaxMode Mode,
    string Currency,
    string? SupplierInvoiceNumber,
    DateOnly? SupplierInvoiceDate,
    string? Narration,
    PurchaseDocumentKind Kind,
    Guid? ReturnsInvoiceId,
    IReadOnlyList<PurchaseInvoiceLineDetail> Lines,
    IReadOnlyList<PurchaseInvoiceChargeDetail> Charges,
    Guid? StockDocumentId,
    Guid? BillId,
    Guid? JournalVoucherId);

/// <summary>Handles <see cref="GetPurchaseInvoiceQuery"/>.</summary>
/// <remarks>
/// Read through the same repository the posting uses rather than through a reader of its
/// own. A purchase is small - a few dozen lines at most - and one screen reading it wants
/// exactly what posting wants.
/// </remarks>
public sealed class GetPurchaseInvoiceQueryHandler
    : IQueryHandler<GetPurchaseInvoiceQuery, PurchaseInvoiceDetail>
{
    private readonly IPurchaseInvoiceRepository _invoices;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetPurchaseInvoiceQueryHandler"/> class.</summary>
    /// <param name="invoices">The purchase invoice repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetPurchaseInvoiceQueryHandler(
        IPurchaseInvoiceRepository invoices,
        ITenantContext tenantContext)
    {
        _invoices = invoices;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<PurchaseInvoiceDetail>> Handle(
        GetPurchaseInvoiceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<PurchaseInvoiceDetail>(Error.Forbidden(
                "PurchaseInvoice.NoFirmSelected",
                "A firm must be selected to read a purchase."));
        }

        PurchaseInvoice? invoice = await _invoices.FindAsync(
            PurchaseInvoiceId.From(request.PurchaseInvoiceId), cancellationToken);

        if (invoice is null || invoice.FirmId != firmId)
        {
            return Result.Failure<PurchaseInvoiceDetail>(Error.NotFound(
                "PurchaseInvoice.NotFound",
                "That purchase does not exist in the selected firm."));
        }

        return Result.Success(new PurchaseInvoiceDetail(
            CreatePurchaseInvoiceCommandHandler.Describe(invoice),
            invoice.Date,
            invoice.SupplierLedgerId.Value,
            invoice.WarehouseId.Value,
            invoice.Mode,
            invoice.Currency.Code,
            invoice.SupplierInvoiceNumber,
            invoice.SupplierInvoiceDate,
            invoice.Narration,
            invoice.Kind,
            invoice.ReturnsInvoiceId?.Value,
            [
                .. invoice.Lines.OrderBy(line => line.LineNumber).Select(line =>
                    new PurchaseInvoiceLineDetail(
                        line.LineNumber,
                        line.ProductId.Value,
                        line.BatchNumber,
                        line.ExpiresOn,
                        line.UnitId.Value,
                        line.Quantity,
                        line.StockQuantity,
                        line.Rate,
                        line.Discount,
                        line.TaxableAmount.Amount,
                        line.TaxAmount.Amount,
                        [
                            .. line.Components.Select(component =>
                                new PurchaseInvoiceLineTaxDetail(
                                    component.Type, component.Percentage, component.Amount)),
                        ],
                        [.. line.Serials.Select(serial => serial.SerialNumber)])),
            ],
            [
                .. invoice.Charges.Select(charge =>
                    new PurchaseInvoiceChargeDetail(
                        charge.LedgerId.Value, charge.Amount.Amount, charge.IsAddition)),
            ],
            invoice.StockDocumentId?.Value,
            invoice.BillId?.Value,
            invoice.JournalVoucherId?.Value));
    }
}
