using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Inventory.Stock;

/// <summary>
/// Applies a posted stock document to the positions it moves, and writes the ledger.
/// </summary>
/// <remarks>
/// <para>
/// The document decides whether it <em>may</em> post; this decides what posting
/// <em>does</em>. They are separate because the second needs aggregates the first is
/// not allowed to hold - a document that could reach into a balance could move stock
/// as a side effect of being saved, and then two documents saved together could each
/// believe they had the last word on the same position.
/// </para>
/// <para>
/// Every kind of document reduces to the same three primitives on a position:
/// goods in at a cost, goods out at whatever the position says they are worth, and
/// goods in at a cost taken from somewhere else. What differs between an issue, a
/// write-off and the outgoing half of a transfer is what it means, not what it does
/// to the arithmetic - which is why they share this and differ only in the table
/// below.
/// </para>
/// </remarks>
internal sealed class StockPoster
{
    private readonly IStockBalanceRepository _balances;
    private readonly IStockLedgerRepository _ledger;

    internal StockPoster(IStockBalanceRepository balances, IStockLedgerRepository ledger)
    {
        _balances = balances;
        _ledger = ledger;
    }

    /// <summary>Moves the stock a posted document says has moved.</summary>
    /// <param name="document">The document, already posted.</param>
    /// <param name="products">Every product it names.</param>
    /// <param name="currency">The firm's base currency.</param>
    /// <param name="postedAtUtc">The instant it was posted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movements written, or the first line that could not move.</returns>
    internal async Task<Result<IReadOnlyList<StockLedgerEntry>>> ApplyAsync(
        StockDocument document,
        IReadOnlyDictionary<ProductId, Product> products,
        CurrencyCode currency,
        DateTimeOffset postedAtUtc,
        CancellationToken cancellationToken)
    {
        List<ProductId> productIds = [.. document.Lines.Select(line => line.ProductId)];

        PositionSet source = await LoadAsync(
            document, document.WarehouseId, productIds, currency, cancellationToken);

        PositionSet? destination = document.DestinationWarehouseId is { } into
            ? await LoadAsync(document, into, productIds, currency, cancellationToken)
            : null;

        List<StockLedgerEntry> written = [];

        foreach (StockDocumentLine line in document.Lines.OrderBy(line => line.LineNumber))
        {
            Result applied = document.Type switch
            {
                StockDocumentType.OpeningStock or StockDocumentType.MaterialReceipt =>
                    Take(written, ReceiveInto(source, document, line, line.Rate, postedAtUtc)),

                StockDocumentType.MaterialIssue or StockDocumentType.DamagedStock =>
                    Take(written, IssueFrom(source, document, line, line.StockQuantity, postedAtUtc)),

                StockDocumentType.StockTransfer =>
                    Transfer(written, source, destination!, document, line, postedAtUtc),

                StockDocumentType.StockAdjustment =>
                    Adjust(written, source, document, line, postedAtUtc),

                StockDocumentType.PhysicalVerification =>
                    Count(written, source, document, line, postedAtUtc),

                _ => Result.Failure(Error.Validation(
                    "StockDocument.UnknownType",
                    $"'{document.Type}' cannot be posted.")),
            };

            if (applied.IsFailure)
            {
                // Named, because "not enough stock" on a forty-line transfer is
                // useless without knowing which of the forty.
                return Result.Failure<IReadOnlyList<StockLedgerEntry>>(
                    Contextualise(applied.Error, line, products));
            }
        }

        foreach (StockLedgerEntry entry in written)
        {
            _ledger.Add(entry);
        }

        return Result.Success<IReadOnlyList<StockLedgerEntry>>(written);
    }

    /// <summary>Undoes what a document did, at the cost each movement was valued at.</summary>
    /// <param name="document">The document being cancelled.</param>
    /// <param name="movements">The entries it originally wrote.</param>
    /// <param name="products">Every product it names.</param>
    /// <param name="currency">The firm's base currency.</param>
    /// <param name="reversedAtUtc">The instant the reversal is posted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the reason the reversal cannot be made.</returns>
    /// <remarks>
    /// Driven from the ledger rather than from the lines. The lines say what somebody
    /// entered; the entries say what the system did with it, including the cost a
    /// transfer or an issue was valued at - which was never on a line and cannot be
    /// recovered from today's average.
    /// </remarks>
    internal async Task<Result> ReverseAsync(
        StockDocument document,
        IReadOnlyList<StockLedgerEntry> movements,
        IReadOnlyDictionary<ProductId, Product> products,
        CurrencyCode currency,
        DateTimeOffset reversedAtUtc,
        CancellationToken cancellationToken)
    {
        // Newest first. A transfer put goods into the destination after taking them
        // out of the source; undoing it in the same order would try to take them out
        // of a destination that had not yet received them.
        List<StockLedgerEntry> ordered = [.. movements.OrderByDescending(entry => entry.PostedAtUtc)
            .ThenByDescending(entry => entry.Id.Value)];

        Dictionary<(WarehouseId, ProductId), StockBalance> loaded = [];

        foreach (WarehouseId warehouse in ordered.Select(entry => entry.WarehouseId).Distinct())
        {
            List<ProductId> productIds =
                [.. ordered.Where(entry => entry.WarehouseId == warehouse)
                    .Select(entry => entry.ProductId).Distinct()];

            IReadOnlyDictionary<ProductId, StockBalance> positions =
                await _balances.GetPositionsAsync(
                    document.FirmId, warehouse, productIds, cancellationToken);

            foreach ((ProductId productId, StockBalance balance) in positions)
            {
                loaded[(warehouse, productId)] = balance;
            }
        }

        List<StockLedgerEntry> contras = [];

        foreach (StockLedgerEntry entry in ordered)
        {
            if (!loaded.TryGetValue((entry.WarehouseId, entry.ProductId), out StockBalance? balance))
            {
                return Result.Failure(Error.BusinessRule(
                    "StockDocument.PositionMissing",
                    "The stock position this document moved no longer exists."));
            }

            // A movement in is undone at the cost it came in at; a movement out is
            // undone by putting the goods back at the cost they left at. Neither uses
            // today's average, which has moved on and is not what this document did.
            Result<Money> reversed = entry.Quantity > 0m
                ? balance.ReverseReceipt(entry.Quantity, entry.UnitCost, reversedAtUtc)
                : balance.Receive(-entry.Quantity, entry.UnitCost, reversedAtUtc);

            if (reversed.IsFailure)
            {
                return Result.Failure(Contextualise(reversed.Error, entry, products));
            }

            Result<StockLedgerEntry> contra = StockLedgerEntry.Record(
                balance,
                document.Date,
                document,
                -entry.Quantity,
                entry.UnitCost,
                entry.Quantity > 0m ? -reversed.Value : reversed.Value,
                reversedAtUtc,
                $"Reversal of {document.Number}");

            if (contra.IsFailure)
            {
                return Result.Failure(contra.Error);
            }

            contras.Add(contra.Value);
        }

        foreach (StockLedgerEntry contra in contras)
        {
            _ledger.Add(contra);
        }

        return Result.Success();
    }

    private static Result Take(List<StockLedgerEntry> written, Result<StockLedgerEntry> entry)
    {
        if (entry.IsFailure)
        {
            return Result.Failure(entry.Error);
        }

        written.Add(entry.Value);

        return Result.Success();
    }

    private static Result<StockLedgerEntry> ReceiveInto(
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        decimal unitCost,
        DateTimeOffset postedAtUtc)
    {
        StockBalance balance = positions.For(line.ProductId);

        Result<Money> received = balance.Receive(line.StockQuantity, unitCost, postedAtUtc);

        return received.IsFailure
            ? Result.Failure<StockLedgerEntry>(received.Error)
            : StockLedgerEntry.Record(
                balance, document.Date, document, line.StockQuantity, unitCost,
                received.Value, postedAtUtc, line.Remarks ?? document.Narration);
    }

    private static Result<StockLedgerEntry> IssueFrom(
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        decimal quantity,
        DateTimeOffset postedAtUtc)
    {
        StockBalance balance = positions.For(line.ProductId);

        // Read before the issue, not after. The issue does not move the average, but
        // relying on that here would make this quietly wrong the day something else
        // does.
        decimal unitCost = balance.AverageCost;

        Result<Money> issued = balance.Issue(quantity, postedAtUtc);

        return issued.IsFailure
            ? Result.Failure<StockLedgerEntry>(issued.Error)
            : StockLedgerEntry.Record(
                balance, document.Date, document, -quantity, unitCost,
                -issued.Value, postedAtUtc, line.Remarks ?? document.Narration);
    }

    /// <summary>Moves goods between two positions at the cost they leave at.</summary>
    /// <remarks>
    /// The receiving side is given the source's average rather than any rate on the
    /// document. A transfer is not a purchase: the firm still owns the same goods at
    /// the same cost, and letting a transfer restate that cost would be a way to
    /// revalue stock by moving it back and forth.
    /// </remarks>
    private static Result Transfer(
        List<StockLedgerEntry> written,
        PositionSet source,
        PositionSet destination,
        StockDocument document,
        StockDocumentLine line,
        DateTimeOffset postedAtUtc)
    {
        decimal unitCost = source.For(line.ProductId).AverageCost;

        Result outgoing = Take(
            written, IssueFrom(source, document, line, line.StockQuantity, postedAtUtc));

        return outgoing.IsFailure
            ? outgoing
            : Take(written, ReceiveInto(destination, document, line, unitCost, postedAtUtc));
    }

    /// <summary>Corrects a position up or down.</summary>
    /// <remarks>
    /// An increase with no rate is valued at what the position already says the goods
    /// cost, which is the right default: finding three of something on a shelf is not
    /// buying it, and the firm's cost has not changed. A rate is accepted for the case
    /// where it has - stock found that was never costed at all, most often on the
    /// first count after go-live.
    /// </remarks>
    private static Result Adjust(
        List<StockLedgerEntry> written,
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        DateTimeOffset postedAtUtc)
    {
        if (line.StockQuantity > 0m)
        {
            StockBalance balance = positions.For(line.ProductId);
            decimal unitCost = line.Rate > 0m ? line.Rate : balance.AverageCost;

            return Take(written, ReceiveInto(positions, document, line, unitCost, postedAtUtc));
        }

        return Take(
            written, IssueFrom(positions, document, line, -line.StockQuantity, postedAtUtc));
    }

    /// <summary>Posts the difference between what was counted and what was expected.</summary>
    /// <remarks>
    /// The line holds the count, not the movement. A count that agrees with the system
    /// moves nothing and writes no ledger entry - recording a movement of zero would
    /// put a row in the stock ledger saying nothing happened, which is true and
    /// useless.
    /// <para>
    /// Stock found is valued at what the position already says it cost, because a
    /// count carries no rate: it is a correction to a quantity, not a statement about
    /// price. Counting a product that has never been received therefore puts it on at
    /// nothing, which is honest - nobody has said what it cost. Establishing an
    /// opening position with a value is what the opening-stock document is for.
    /// </para>
    /// </remarks>
    private static Result Count(
        List<StockLedgerEntry> written,
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        DateTimeOffset postedAtUtc)
    {
        StockBalance balance = positions.For(line.ProductId);
        decimal difference = line.StockQuantity - balance.Quantity;

        if (difference == 0m)
        {
            return Result.Success();
        }

        if (difference > 0m)
        {
            Result<Money> received = balance.Receive(
                difference, balance.AverageCost, postedAtUtc);

            if (received.IsFailure)
            {
                return Result.Failure(received.Error);
            }

            Result<StockLedgerEntry> entry = StockLedgerEntry.Record(
                balance, document.Date, document, difference, balance.AverageCost,
                received.Value, postedAtUtc, line.Remarks ?? document.Narration);

            return Take(written, entry);
        }

        decimal unitCost = balance.AverageCost;
        Result<Money> issued = balance.Issue(-difference, postedAtUtc);

        if (issued.IsFailure)
        {
            return Result.Failure(issued.Error);
        }

        return Take(
            written,
            StockLedgerEntry.Record(
                balance, document.Date, document, difference, unitCost,
                -issued.Value, postedAtUtc, line.Remarks ?? document.Narration));
    }

    private static Error Contextualise(
        Error error,
        StockDocumentLine line,
        IReadOnlyDictionary<ProductId, Product> products) =>
        Name(error, line.ProductId, products);

    private static Error Contextualise(
        Error error,
        StockLedgerEntry entry,
        IReadOnlyDictionary<ProductId, Product> products) =>
        Name(error, entry.ProductId, products);

    private static Error Name(
        Error error,
        ProductId productId,
        IReadOnlyDictionary<ProductId, Product> products) =>
        products.TryGetValue(productId, out Product? product)
            ? error with { Description = $"{product.Code}: {error.Description}" }
            : error;

    private async Task<PositionSet> LoadAsync(
        StockDocument document,
        WarehouseId warehouseId,
        IReadOnlyCollection<ProductId> productIds,
        CurrencyCode currency,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<ProductId, StockBalance> existing =
            await _balances.GetPositionsAsync(
                document.FirmId, warehouseId, productIds, cancellationToken);

        return new PositionSet(
            document, warehouseId, currency, new Dictionary<ProductId, StockBalance>(existing),
            _balances);
    }

    /// <summary>The positions of one warehouse, opening any that do not exist yet.</summary>
    private sealed class PositionSet
    {
        private readonly StockDocument _document;
        private readonly WarehouseId _warehouseId;
        private readonly CurrencyCode _currency;
        private readonly Dictionary<ProductId, StockBalance> _balances;
        private readonly IStockBalanceRepository _repository;

        internal PositionSet(
            StockDocument document,
            WarehouseId warehouseId,
            CurrencyCode currency,
            Dictionary<ProductId, StockBalance> balances,
            IStockBalanceRepository repository)
        {
            _document = document;
            _warehouseId = warehouseId;
            _currency = currency;
            _balances = balances;
            _repository = repository;
        }

        internal StockBalance For(ProductId productId)
        {
            if (_balances.TryGetValue(productId, out StockBalance? balance))
            {
                return balance;
            }

            // Opened on first use rather than up front for every product in every
            // warehouse. A firm with ten thousand products and six godowns trades in a
            // fraction of the sixty thousand combinations, and rows for the rest would
            // be noise in every report that reads them.
            StockBalance opened = StockBalance.Open(
                _document.TenantId, _document.FirmId, productId, _warehouseId, _currency);

            _repository.Add(opened);
            _balances[productId] = opened;

            return opened;
        }
    }
}
