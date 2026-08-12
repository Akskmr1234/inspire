using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;

namespace ERP.Application.Sales;

/// <summary>Turns what a sales document moved into the stock document that moved it.</summary>
/// <remarks>
/// <para>
/// The second of the two documents a sale leaves. The invoice says what was sold and for
/// how much; the stock document says what left the warehouse - or came back into it - and
/// it is that document the stock ledger records, so the batch and the units a line named
/// have to travel across intact, or a serialised handset would be billed to a customer
/// and still be on the shelf.
/// </para>
/// <para>
/// An issue carries no rate. What goods are worth on the way out is the firm's own
/// average cost, which the stock poster reads from the position; passing the selling
/// price would value the issue at what the customer paid and turn every sale into a stock
/// gain equal to its own margin.
/// </para>
/// <para>
/// A return does carry one, because goods coming back have to be valued at something and
/// the position cannot say what. The business's answer of 2026-08-12: at the cost they
/// went out at where the return names its invoice, so the cost-of-goods-sold reversal
/// matches the original to the fils; at the current average where it names none, which
/// leaves the average untouched because receiving at the average cannot move it.
/// </para>
/// </remarks>
internal static class SalesIssue
{
    /// <summary>Builds the stock document for a posted sales document.</summary>
    /// <param name="invoice">The document being posted.</param>
    /// <param name="warehouse">The warehouse the goods move through.</param>
    /// <param name="products">Every product the lines name.</param>
    /// <param name="units">Every unit the lines name.</param>
    /// <param name="batches">Every batch the lines name.</param>
    /// <param name="serials">Every serialised unit the lines name.</param>
    /// <param name="year">The financial year it falls in.</param>
    /// <param name="number">The number its own series issued.</param>
    /// <param name="costs">What each product comes back in at. Empty for an issue.</param>
    /// <returns>The draft stock document, or the first line that could not be built.</returns>
    internal static Result<StockDocument> Build(
        SalesInvoice invoice,
        Warehouse warehouse,
        IReadOnlyDictionary<ProductId, Product> products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units,
        IReadOnlyDictionary<BatchId, Batch> batches,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials,
        FinancialYear year,
        string number,
        IReadOnlyDictionary<ProductId, decimal>? costs = null)
    {
        Result<StockDocument> draft = StockDocument.CreateDraft(
            invoice.TenantId,
            invoice.FirmId,
            year,
            invoice.IsReturn ? StockDocumentType.SalesReturn : StockDocumentType.SalesIssue,
            number,
            invoice.Date,
            warehouse);

        if (draft.IsFailure)
        {
            return draft;
        }

        StockDocument document = draft.Value;

        foreach (SalesInvoiceLine line in invoice.Lines.OrderBy(line => line.LineNumber))
        {
            if (!products.TryGetValue(line.ProductId, out Product? product))
            {
                return Result.Failure<StockDocument>(Error.NotFound(
                    "SalesInvoice.ProductNotFound",
                    $"A product on line {line.LineNumber} no longer exists."));
            }

            if (!units.TryGetValue(line.UnitId, out UnitOfMeasure? unit))
            {
                return Result.Failure<StockDocument>(Error.NotFound(
                    "SalesInvoice.UnitNotFound",
                    $"The unit on line {line.LineNumber} no longer exists."));
            }

            Batch? batch = null;

            if (line.BatchId is { } batchId && !batches.TryGetValue(batchId, out batch))
            {
                return Result.Failure<StockDocument>(Error.NotFound(
                    "SalesInvoice.BatchNotFound",
                    $"The batch on line {line.LineNumber} no longer exists."));
            }

            Result<IReadOnlyCollection<SerialNumber>> units_ = Resolve(line, serials);

            if (units_.IsFailure)
            {
                return Result.Failure<StockDocument>(units_.Error);
            }

            // The quantity in stock units, which the invoice line already carries: the
            // invoice converted it once when the line was entered, and converting it
            // again here would be a second chance to disagree with the printed document.
            Result<StockDocumentLine> added = document.AddLine(
                product,
                unit,
                line.Quantity,
                line.StockQuantity,
                rate: costs?.GetValueOrDefault(line.ProductId) ?? 0m,
                batch,
                units_.Value,
                remarks: invoice.IsReturn
                    ? $"Sales return {invoice.Number}"
                    : $"Sales invoice {invoice.Number}");

            if (added.IsFailure)
            {
                return Result.Failure<StockDocument>(added.Error);
            }
        }

        return Result.Success(document);
    }

    /// <summary>Collects the units a line sells, refusing one that has gone missing.</summary>
    private static Result<IReadOnlyCollection<SerialNumber>> Resolve(
        SalesInvoiceLine line,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials)
    {
        List<SerialNumber> found = [];

        foreach (SalesInvoiceLineSerial named in line.Serials)
        {
            if (!serials.TryGetValue(named.SerialNumberId, out SerialNumber? serial))
            {
                return Result.Failure<IReadOnlyCollection<SerialNumber>>(Error.NotFound(
                    "SalesInvoice.SerialNotFound",
                    $"A unit named on line {line.LineNumber} no longer exists."));
            }

            found.Add(serial);
        }

        return Result.Success<IReadOnlyCollection<SerialNumber>>(found);
    }
}
