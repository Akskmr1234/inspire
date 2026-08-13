using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;

namespace ERP.Application.Purchase;

/// <summary>Turns what a purchase document moved into the stock document that moved it.</summary>
/// <remarks>
/// <para>
/// The second of the two documents a purchase leaves, and unlike the sales side this one
/// is expressed as the ordinary stock command rather than assembled by hand. That is
/// deliberate: a purchase is where batches are opened and serial numbers are written down
/// for the first time, and all of that machinery already exists behind
/// <see cref="CreateStockDocumentCommand"/>. Rebuilding it here would be a second
/// implementation of section 10 to keep in step with the first.
/// </para>
/// <para>
/// The rate is what the goods cost per stock unit, net of the line's discount. It is the
/// one figure the receipt has to carry: average costing consumes it, and it is also what
/// makes the clearing account net to nothing, because the value the receipt credits to
/// Goods Received is the same taxable amount the invoice will debit back.
/// </para>
/// </remarks>
internal static class PurchaseReceipt
{
    /// <summary>Describes the stock movement a posted purchase document makes.</summary>
    /// <param name="invoice">The document being posted.</param>
    /// <returns>The stock command, ready for the ordinary loader to build.</returns>
    internal static CreateStockDocumentCommand Describe(PurchaseInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new CreateStockDocumentCommand(
            invoice.IsReturn
                ? StockDocumentType.PurchaseReturn
                : StockDocumentType.PurchaseReceipt,
            invoice.Date,
            invoice.WarehouseId.Value,
            [
                .. invoice.Lines.OrderBy(line => line.LineNumber)
                    .Select(line => Describe(line, carriesRate: !invoice.IsReturn)),
            ],
            DestinationWarehouseId: null,
            ReferenceNumber: invoice.SupplierInvoiceNumber,
            Narration: invoice.IsReturn
                ? $"Purchase return {invoice.Number}"
                : $"Purchase invoice {invoice.Number}",
            PostImmediately: true);
    }

    /// <summary>Describes one line of it.</summary>
    /// <remarks>
    /// A return carries no rate. Goods going back leave at what the position says they
    /// cost, which is how every other issue is valued - passing the price agreed with the
    /// supplier would revalue the shelf from a line of a document about something that has
    /// already left it.
    /// </remarks>
    private static StockDocumentLineInput Describe(PurchaseInvoiceLine line, bool carriesRate) =>
        new(
            line.ProductId.Value,
            line.Quantity,
            line.UnitId.Value,
            Rate: carriesRate ? CostPerStockUnit(line) : 0m,
            Remarks: null,
            BatchId: null,
            BatchNumber: line.BatchNumber,
            ManufacturedOn: null,
            ExpiresOn: line.ExpiresOn,
            SerialNumbers: [.. line.Serials.Select(serial => serial.SerialNumber)],
            WarrantyUntil: null);

    /// <summary>What one stock unit cost, net of the discount taken off the line.</summary>
    /// <remarks>
    /// Divided out rather than taken from the entered rate, because the entered rate is
    /// per entry unit and before the discount. A carton bought at 120 less 20, holding ten
    /// units, cost ten each; valuing the shelf at twelve would put the discount into stock
    /// and take it out again as a margin on the way past.
    /// </remarks>
    private static decimal CostPerStockUnit(PurchaseInvoiceLine line) =>
        line.StockQuantity == 0m
            ? 0m
            : decimal.Round(
                line.TaxableAmount.Amount / line.StockQuantity,
                StockBalance.CostScale,
                MidpointRounding.AwayFromZero);
}
