using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;

namespace ERP.Application.Inventory.Stock;

/// <summary>
/// The checks an invoice passes before it bills a delivery note, or a purchase before it
/// pays for a receipt note, instead of moving the goods itself.
/// </summary>
/// <remarks>
/// The goods moved with the note, so the invoice must describe the same goods: the same
/// customer or supplier, every product in the same quantity, and only once. Anything else
/// would leave the stock ledger and the bill telling two different stories about one
/// delivery.
/// </remarks>
internal static class NoteBilling
{
    /// <summary>Checks that an invoice may stand in for a note's stock movement.</summary>
    /// <param name="note">The note, or null where it no longer exists.</param>
    /// <param name="expected">Which kind of note the invoice may bill.</param>
    /// <param name="party">The invoice's customer or supplier.</param>
    /// <param name="billed">The invoice's stocked lines, in stock units.</param>
    /// <param name="products">The products, for naming one whose quantity differs.</param>
    /// <param name="alreadyBilledBy">Another invoice that already bills the note, if any.</param>
    /// <returns>Success, or the first reason the invoice cannot bill the note.</returns>
    internal static Result Check(
        StockDocument? note,
        StockDocumentType expected,
        LedgerId party,
        IEnumerable<(ProductId Product, decimal StockQuantity)> billed,
        IReadOnlyDictionary<ProductId, Product> products,
        string? alreadyBilledBy)
    {
        string kind = expected == StockDocumentType.DeliveryNote ? "delivery note" : "receipt note";

        if (note is null || note.Type != expected)
        {
            return Result.Failure(Error.NotFound(
                "StockDocument.NoteNotFound", $"The {kind} this invoice bills no longer exists."));
        }

        if (note.Status != StockDocumentStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NoteNotPosted",
                $"The {kind} '{note.Number}' is {note.Status}; only a posted note can be billed."));
        }

        if (note.PartyLedgerId != party)
        {
            return Result.Failure(Error.Validation(
                "StockDocument.NoteOtherParty",
                $"The {kind} '{note.Number}' names a different "
                + (expected == StockDocumentType.DeliveryNote ? "customer." : "supplier.")));
        }

        if (alreadyBilledBy is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NoteAlreadyBilled",
                $"The {kind} '{note.Number}' is already billed by '{alreadyBilledBy}'."));
        }

        Dictionary<ProductId, decimal> moved = note.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.StockQuantity));

        Dictionary<ProductId, decimal> invoiced = billed
            .GroupBy(line => line.Product)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.StockQuantity));

        foreach (ProductId product in moved.Keys.Union(invoiced.Keys))
        {
            decimal went = moved.GetValueOrDefault(product);
            decimal charged = invoiced.GetValueOrDefault(product);

            if (went != charged)
            {
                string code = products.TryGetValue(product, out Product? found) ? found.Code : product.ToString();

                return Result.Failure(Error.BusinessRule(
                    "StockDocument.NoteQuantityDiffers",
                    $"'{code}': the {kind} '{note.Number}' moved {went:0.######} and the invoice "
                    + $"bills {charged:0.######}. An invoice against a note bills exactly what moved."));
            }
        }

        return Result.Success();
    }
}
