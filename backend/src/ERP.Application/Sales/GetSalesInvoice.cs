using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Sales;

/// <summary>Reads one invoice, with its lines, its charges, and what posting it produced.</summary>
/// <param name="SalesInvoiceId">The invoice.</param>
public sealed record GetSalesInvoiceQuery(Guid SalesInvoiceId) : IQuery<SalesInvoiceDetail>;

/// <summary>One tax head as it was charged on a line.</summary>
/// <param name="Component">The head: VAT, CGST, SGST, IGST, cess.</param>
/// <param name="Percentage">The rate it was charged at.</param>
/// <param name="Amount">What that came to.</param>
public sealed record SalesInvoiceLineTaxDetail(
    TaxComponentType Component,
    decimal Percentage,
    decimal Amount);

/// <summary>One line of an invoice.</summary>
/// <param name="LineNumber">Its position, from one.</param>
/// <param name="ProductId">The product sold.</param>
/// <param name="BatchId">The batch it came out of, where the product is batched.</param>
/// <param name="UnitId">The unit the quantity was entered in.</param>
/// <param name="Quantity">How many, in that unit.</param>
/// <param name="StockQuantity">The same, in the product's stock unit.</param>
/// <param name="Rate">What one went for.</param>
/// <param name="Discount">What came off before tax.</param>
/// <param name="Taxable">What the line comes to before tax.</param>
/// <param name="Tax">The tax on it.</param>
/// <param name="Components">That tax, head by head, as the engine assessed it.</param>
/// <param name="SerialNumberIds">The units sold, where the product is serialised.</param>
public sealed record SalesInvoiceLineDetail(
    int LineNumber,
    Guid ProductId,
    Guid? BatchId,
    Guid UnitId,
    decimal Quantity,
    decimal StockQuantity,
    decimal Rate,
    decimal Discount,
    decimal Taxable,
    decimal Tax,
    IReadOnlyList<SalesInvoiceLineTaxDetail> Components,
    IReadOnlyList<Guid> SerialNumberIds);

/// <summary>One charge carried beside the goods.</summary>
/// <param name="LedgerId">The account it posts to.</param>
/// <param name="Amount">What it comes to, always positive.</param>
/// <param name="IsAddition">Whether it adds to the total rather than deducting.</param>
public sealed record SalesInvoiceChargeDetail(Guid LedgerId, decimal Amount, bool IsAddition);

/// <summary>An invoice in full.</summary>
/// <param name="Header">Its number, status and figures.</param>
/// <param name="Date">The invoice date.</param>
/// <param name="CustomerLedgerId">The customer billed.</param>
/// <param name="WarehouseId">The warehouse the goods leave.</param>
/// <param name="Mode">The tax mode it was entered under.</param>
/// <param name="Currency">The currency it is stated in.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="Narration">What is printed on it.</param>
/// <param name="Lines">What was sold.</param>
/// <param name="Charges">What was carried beside it.</param>
/// <param name="StockDocumentId">The issue that took the goods off the shelf, once posted.</param>
/// <param name="BillId">The bill the customer owes, once posted.</param>
/// <param name="JournalVoucherId">The journal in the nominal ledger, once posted.</param>
public sealed record SalesInvoiceDetail(
    SalesInvoiceResponse Header,
    DateOnly Date,
    Guid CustomerLedgerId,
    Guid WarehouseId,
    TaxMode Mode,
    string Currency,
    string? ReferenceNumber,
    string? Narration,
    IReadOnlyList<SalesInvoiceLineDetail> Lines,
    IReadOnlyList<SalesInvoiceChargeDetail> Charges,
    Guid? StockDocumentId,
    Guid? BillId,
    Guid? JournalVoucherId);

/// <summary>Handles <see cref="GetSalesInvoiceQuery"/>.</summary>
/// <remarks>
/// Read through the same repository the posting uses rather than through a reader of its
/// own. An invoice is small - a few dozen lines at most - and one screen reading it wants
/// exactly what posting wants: the lines, their tax per head, the charges, and the units.
/// </remarks>
public sealed class GetSalesInvoiceQueryHandler
    : IQueryHandler<GetSalesInvoiceQuery, SalesInvoiceDetail>
{
    private readonly ISalesInvoiceRepository _invoices;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetSalesInvoiceQueryHandler"/> class.</summary>
    /// <param name="invoices">The sales invoice repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetSalesInvoiceQueryHandler(
        ISalesInvoiceRepository invoices,
        ITenantContext tenantContext)
    {
        _invoices = invoices;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<SalesInvoiceDetail>> Handle(
        GetSalesInvoiceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<SalesInvoiceDetail>(Error.Forbidden(
                "SalesInvoice.NoFirmSelected",
                "A firm must be selected to read an invoice."));
        }

        SalesInvoice? invoice = await _invoices.FindAsync(
            SalesInvoiceId.From(request.SalesInvoiceId), cancellationToken);

        if (invoice is null || invoice.FirmId != firmId)
        {
            return Result.Failure<SalesInvoiceDetail>(Error.NotFound(
                "SalesInvoice.NotFound", "That invoice does not exist in the selected firm."));
        }

        return Result.Success(new SalesInvoiceDetail(
            CreateSalesInvoiceCommandHandler.Describe(invoice),
            invoice.Date,
            invoice.CustomerLedgerId.Value,
            invoice.WarehouseId.Value,
            invoice.Mode,
            invoice.Currency.Code,
            invoice.ReferenceNumber,
            invoice.Narration,
            [
                .. invoice.Lines.OrderBy(line => line.LineNumber).Select(line =>
                    new SalesInvoiceLineDetail(
                        line.LineNumber,
                        line.ProductId.Value,
                        line.BatchId?.Value,
                        line.UnitId.Value,
                        line.Quantity,
                        line.StockQuantity,
                        line.Rate,
                        line.Discount,
                        line.TaxableAmount.Amount,
                        line.TaxAmount.Amount,
                        [
                            .. line.Components.Select(component =>
                                new SalesInvoiceLineTaxDetail(
                                    component.Type, component.Percentage, component.Amount)),
                        ],
                        [.. line.Serials.Select(serial => serial.SerialNumberId.Value)])),
            ],
            [
                .. invoice.Charges.Select(charge =>
                    new SalesInvoiceChargeDetail(
                        charge.LedgerId.Value, charge.Amount.Amount, charge.IsAddition)),
            ],
            invoice.StockDocumentId?.Value,
            invoice.BillId?.Value,
            invoice.JournalVoucherId?.Value));
    }
}
