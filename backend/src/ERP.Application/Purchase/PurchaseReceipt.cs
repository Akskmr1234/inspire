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
    /// <summary>The places a product's cost is held to.</summary>
    private const int ProductCostScale = 4;

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

    /// <summary>
    /// Sets the cost of every product costed at its last purchase rate to what this
    /// purchase paid for it.
    /// </summary>
    /// <param name="invoice">The purchase just posted.</param>
    /// <param name="products">Its products, loaded for the receipt.</param>
    /// <param name="everyProduct">
    /// Whether every product takes the rate, whatever it is costed at - the firm's "update
    /// product rate" setting. A product carrying a profit percentage then has its retail
    /// rate rebuilt on the new cost, so the shelf price follows what the goods now cost.
    /// </param>
    /// <returns>How many products were repriced.</returns>
    /// <remarks>
    /// The rate per stock unit, net of the line's discount - the same figure the
    /// receipt values the shelf at. A product bought on two lines of one invoice takes
    /// the later line's. A return changes nothing: the supplier's price was not what
    /// it was bought at, only what it went back at.
    /// </remarks>
    internal static int ApplyLastPurchaseRates(
        PurchaseInvoice invoice,
        IReadOnlyDictionary<ProductId, Product> products,
        bool everyProduct = false)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(products);

        if (invoice.IsReturn)
        {
            return 0;
        }

        int repriced = 0;

        foreach (PurchaseInvoiceLine line in invoice.Lines.OrderBy(line => line.LineNumber))
        {
            if (line.StockQuantity <= 0m
                || !products.TryGetValue(line.ProductId, out Product? product)
                || (!everyProduct && product.CostingMethod != CostingMethod.LastPurchaseRate))
            {
                continue;
            }

            decimal cost = decimal.Round(
                CostPerStockUnit(line), ProductCostScale, MidpointRounding.AwayFromZero);

            ProductRates rates = product.Rates with { Cost = cost };

            if (everyProduct && rates.ProfitPercentage > 0m)
            {
                decimal retail = decimal.Round(
                    cost * (1m + (rates.ProfitPercentage / 100m)),
                    ProductCostScale,
                    MidpointRounding.AwayFromZero);

                // Never above a printed MRP: the label on the pack is the most it may sell for.
                rates = rates with
                {
                    RetailRate = rates.MaximumRetailPrice > 0m
                        ? Math.Min(retail, rates.MaximumRetailPrice)
                        : retail,
                };
            }

            product.SetRates(rates);
            repriced++;
        }

        return repriced;
    }

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
