using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;

namespace ERP.Application.Sales;

/// <summary>Turns what an invoice sold into the issue that takes it off the shelf.</summary>
/// <remarks>
/// <para>
/// The second of the two documents a sale leaves. The invoice says what was sold and for
/// how much; the issue says what left the warehouse, and it is the issue the stock ledger
/// records - so the batch and the units a line named have to travel across intact, or a
/// serialised handset would be billed to a customer and still be on the shelf.
/// </para>
/// <para>
/// No rate is carried. What goods are worth on the way out is the firm's own average
/// cost, which the stock poster reads from the position; passing the selling price would
/// value the issue at what the customer paid and turn every sale into a stock gain equal
/// to its own margin.
/// </para>
/// </remarks>
internal static class SalesIssue
{
    /// <summary>Builds the issue for a posted invoice.</summary>
    /// <param name="invoice">The invoice being posted.</param>
    /// <param name="warehouse">The warehouse the goods leave.</param>
    /// <param name="products">Every product the lines name.</param>
    /// <param name="units">Every unit the lines name.</param>
    /// <param name="batches">Every batch the lines name.</param>
    /// <param name="serials">Every serialised unit the lines name.</param>
    /// <param name="year">The financial year it falls in.</param>
    /// <param name="number">The number its own series issued.</param>
    /// <returns>The draft issue, or the first line that could not be built.</returns>
    internal static Result<StockDocument> Build(
        SalesInvoice invoice,
        Warehouse warehouse,
        IReadOnlyDictionary<ProductId, Product> products,
        IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure> units,
        IReadOnlyDictionary<BatchId, Batch> batches,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials,
        FinancialYear year,
        string number)
    {
        Result<StockDocument> draft = StockDocument.CreateDraft(
            invoice.TenantId,
            invoice.FirmId,
            year,
            StockDocumentType.SalesIssue,
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
                rate: 0m,
                batch,
                units_.Value,
                remarks: $"Sales invoice {invoice.Number}");

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
