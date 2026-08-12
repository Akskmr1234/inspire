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
    private readonly IBatchBalanceRepository _batchBalances;
    private readonly IStockLedgerRepository _ledger;

    internal StockPoster(
        IStockBalanceRepository balances,
        IBatchBalanceRepository batchBalances,
        IStockLedgerRepository ledger)
    {
        _balances = balances;
        _batchBalances = batchBalances;
        _ledger = ledger;
    }

    /// <summary>Moves the stock a posted document says has moved.</summary>
    /// <param name="document">The document, already posted.</param>
    /// <param name="products">Every product it names.</param>
    /// <param name="batches">Every batch it names.</param>
    /// <param name="serials">Every serialised unit it names.</param>
    /// <param name="currency">The firm's base currency.</param>
    /// <param name="postedAtUtc">The instant it was posted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movements written, or the first line that could not move.</returns>
    /// <remarks>
    /// A batched line moves two positions rather than one: the batch's, at what that
    /// batch costs, and the product's, by the same quantity and the same value. Both,
    /// always, in the same direction - which is what keeps the product position equal
    /// to the sum of its batches and the two valuations equal to each other.
    /// </remarks>
    internal async Task<Result<IReadOnlyList<StockLedgerEntry>>> ApplyAsync(
        StockDocument document,
        IReadOnlyDictionary<ProductId, Product> products,
        IReadOnlyDictionary<BatchId, Batch> batches,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials,
        CurrencyCode currency,
        DateTimeOffset postedAtUtc,
        CancellationToken cancellationToken)
    {
        List<ProductId> productIds = [.. document.Lines.Select(line => line.ProductId)];
        List<BatchId> batchIds =
            [.. document.Lines.Where(line => line.BatchId is not null)
                .Select(line => line.BatchId!.Value)];

        PositionSet source = await LoadAsync(
            document, document.WarehouseId, productIds, batchIds, batches, currency,
            cancellationToken);

        PositionSet? destination = document.DestinationWarehouseId is { } into
            ? await LoadAsync(
                document, into, productIds, batchIds, batches, currency, cancellationToken)
            : null;

        List<StockLedgerEntry> written = [];

        foreach (StockDocumentLine line in document.Lines.OrderBy(line => line.LineNumber))
        {
            Result applied = document.Type switch
            {
                StockDocumentType.OpeningStock or StockDocumentType.MaterialReceipt
                    or StockDocumentType.SalesReturn =>
                    Take(
                        written,
                        ReceiveInto(
                            source, document, line, line.StockQuantity, line.Rate, postedAtUtc)),

                StockDocumentType.MaterialIssue or StockDocumentType.DamagedStock
                    or StockDocumentType.SalesIssue =>
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

            Result moved = MoveUnits(document, line, serials);

            if (moved.IsFailure)
            {
                return Result.Failure<IReadOnlyList<StockLedgerEntry>>(
                    Contextualise(moved.Error, line, products));
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
    /// <param name="batches">Every batch it names.</param>
    /// <param name="serials">Every serialised unit it names.</param>
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
        IReadOnlyDictionary<BatchId, Batch> batches,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials,
        CurrencyCode currency,
        DateTimeOffset reversedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);

        Result units = RestoreUnits(document, serials);

        if (units.IsFailure)
        {
            return units;
        }

        // Newest first. A transfer put goods into the destination after taking them
        // out of the source; undoing it in the same order would try to take them out
        // of a destination that had not yet received them.
        List<StockLedgerEntry> ordered = [.. movements.OrderByDescending(entry => entry.PostedAtUtc)
            .ThenByDescending(entry => entry.Id.Value)];

        Dictionary<(WarehouseId, ProductId), StockBalance> loaded = [];
        Dictionary<(WarehouseId, BatchId), BatchBalance> loadedBatches = [];

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

            List<BatchId> batchIds =
                [.. ordered.Where(entry => entry.WarehouseId == warehouse && entry.BatchId is not null)
                    .Select(entry => entry.BatchId!.Value).Distinct()];

            if (batchIds.Count == 0)
            {
                continue;
            }

            IReadOnlyDictionary<BatchId, BatchBalance> batchPositions =
                await _batchBalances.GetPositionsAsync(
                    document.FirmId, warehouse, batchIds, cancellationToken);

            foreach ((BatchId batchId, BatchBalance balance) in batchPositions)
            {
                loadedBatches[(warehouse, batchId)] = balance;
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

            Batch? batch = null;

            if (entry.BatchId is { } batchId)
            {
                if (!loadedBatches.TryGetValue((entry.WarehouseId, batchId), out BatchBalance? batchBalance)
                    || !batches.TryGetValue(batchId, out batch))
                {
                    return Result.Failure(Error.BusinessRule(
                        "StockDocument.BatchPositionMissing",
                        $"The position of batch '{entry.BatchNumber}' that this document "
                        + "moved no longer exists."));
                }

                // The batch first, so a reversal the batch refuses - goods long since
                // sold out of it - never reaches the product position it would
                // otherwise have moved on its own.
                Result<Money> batchReversed = entry.Quantity > 0m
                    ? batchBalance.ReverseReceipt(entry.Quantity, entry.UnitCost, reversedAtUtc)
                    : batchBalance.Receive(-entry.Quantity, entry.UnitCost, reversedAtUtc);

                if (batchReversed.IsFailure)
                {
                    return Result.Failure(Contextualise(batchReversed.Error, entry, products));
                }
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
                $"Reversal of {document.Number}",
                batch);

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

    /// <summary>Moves the units a line names, in step with the quantity it moved.</summary>
    /// <param name="document">The document being posted.</param>
    /// <param name="line">The line.</param>
    /// <param name="serials">Every unit the document names.</param>
    /// <returns>Success, or the first unit that could not move.</returns>
    /// <remarks>
    /// A serial has no arithmetic - a unit is one unit - so what happens here is a
    /// change of state and of place, not a sum. The direction is taken from the same
    /// document type that decided the quantity, so the two can never disagree about
    /// which way the goods went.
    /// <para>
    /// A receipt of a unit that had already gone out is a customer return rather than a
    /// new arrival. That distinction is worth keeping: the unit's warranty runs from the
    /// day it was first received, and treating the return as a fresh receipt would
    /// restart it.
    /// </para>
    /// </remarks>
    private static Result MoveUnits(
        StockDocument document,
        StockDocumentLine line,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials)
    {
        foreach (StockDocumentLineSerial named in line.Serials)
        {
            if (!serials.TryGetValue(named.SerialNumberId, out SerialNumber? unit))
            {
                return Result.Failure(Error.NotFound(
                    "Serial.NotFound", "A unit this document names no longer exists."));
            }

            Result moved = document.Type switch
            {
                StockDocumentType.StockTransfer =>
                    unit.TransferTo(document.DestinationWarehouseId!.Value, document.Id),

                StockDocumentType.MaterialIssue or StockDocumentType.DamagedStock
                    or StockDocumentType.SalesIssue =>
                    unit.Issue(document.Date, document.Id),

                _ when line.StockQuantity < 0m => unit.Issue(document.Date, document.Id),

                _ => unit.Status == SerialStatus.Issued
                    ? unit.ReturnFromCustomer(document.WarehouseId, document.Date, document.Id)
                    : unit.TakeIntoStock(document.WarehouseId, document.Date, document.Id),
            };

            if (moved.IsFailure)
            {
                return moved;
            }
        }

        return Result.Success();
    }

    /// <summary>Puts the units a cancelled document moved back where they were.</summary>
    /// <param name="document">The document being cancelled.</param>
    /// <param name="serials">Every unit its lines name.</param>
    /// <returns>Success, or the first unit that could not be put back.</returns>
    /// <remarks>
    /// Undone by the document that did it, line by line, rather than by replaying the
    /// stock ledger: the ledger records quantities and the units are not quantities.
    /// A receipt's own units go back to being written down but not held; everything
    /// else returns to the shelf it came from.
    /// </remarks>
    private static Result RestoreUnits(
        StockDocument document,
        IReadOnlyDictionary<SerialNumberId, SerialNumber> serials)
    {
        foreach (StockDocumentLine line in document.Lines)
        {
            foreach (StockDocumentLineSerial named in line.Serials)
            {
                if (!serials.TryGetValue(named.SerialNumberId, out SerialNumber? unit))
                {
                    return Result.Failure(Error.NotFound(
                        "Serial.NotFound", "A unit this document moved no longer exists."));
                }

                Result restored = document.Type switch
                {
                    StockDocumentType.StockTransfer =>
                        unit.TransferTo(document.WarehouseId, document.Id),

                    StockDocumentType.MaterialIssue or StockDocumentType.DamagedStock
                        or StockDocumentType.SalesIssue =>
                        unit.UndoIssue(document.WarehouseId, document.Id),

                    _ when line.StockQuantity < 0m =>
                        unit.UndoIssue(document.WarehouseId, document.Id),

                    _ => unit.OriginDocumentId == document.Id
                        ? unit.UndoReceipt(document.Id)
                        : unit.Issue(document.Date, document.Id),
                };

                if (restored.IsFailure)
                {
                    return restored;
                }
            }
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

    /// <summary>Takes goods in, into the batch first where there is one.</summary>
    /// <remarks>
    /// The batch position is moved before the product's, so a receipt the batch
    /// refuses never reaches the product at all. Both take the same quantity at the
    /// same cost, which is what keeps one the sum of the other.
    /// </remarks>
    private static Result<StockLedgerEntry> ReceiveInto(
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        decimal quantity,
        decimal unitCost,
        DateTimeOffset postedAtUtc)
    {
        if (positions.BatchOf(line) is { } batch)
        {
            Result<Money> intoBatch = positions.ForBatch(batch)
                .Receive(quantity, unitCost, postedAtUtc);

            if (intoBatch.IsFailure)
            {
                return Result.Failure<StockLedgerEntry>(intoBatch.Error);
            }
        }

        StockBalance balance = positions.For(line.ProductId);

        Result<Money> received = balance.Receive(quantity, unitCost, postedAtUtc);

        return received.IsFailure
            ? Result.Failure<StockLedgerEntry>(received.Error)
            : StockLedgerEntry.Record(
                balance, document.Date, document, quantity, unitCost,
                received.Value, postedAtUtc, line.Remarks ?? document.Narration,
                positions.BatchOf(line));
    }

    /// <summary>Takes goods out, at what the batch cost where there is one.</summary>
    /// <remarks>
    /// A batched issue leaves the product position at the batch's cost rather than at
    /// the product's average, because those are the goods that were picked. Removing
    /// them at the average would leave the product position holding a value its
    /// batches no longer add up to.
    /// </remarks>
    private static Result<StockLedgerEntry> IssueFrom(
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        decimal quantity,
        DateTimeOffset postedAtUtc)
    {
        StockBalance balance = positions.For(line.ProductId);
        Batch? batch = positions.BatchOf(line);

        // Read before the issue, not after. Neither issue moves the cost it was read
        // from, but relying on that here would make this quietly wrong the day
        // something else does.
        decimal unitCost = positions.CostOf(line);

        if (batch is not null)
        {
            Result<Money> outOfBatch = positions.ForBatch(batch).Issue(quantity, postedAtUtc);

            if (outOfBatch.IsFailure)
            {
                return Result.Failure<StockLedgerEntry>(outOfBatch.Error);
            }
        }

        Result<Money> issued = batch is null
            ? balance.Issue(quantity, postedAtUtc)
            : balance.IssueAt(quantity, unitCost, postedAtUtc);

        return issued.IsFailure
            ? Result.Failure<StockLedgerEntry>(issued.Error)
            : StockLedgerEntry.Record(
                balance, document.Date, document, -quantity, unitCost,
                -issued.Value, postedAtUtc, line.Remarks ?? document.Narration, batch);
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
        decimal unitCost = source.CostOf(line);

        Result outgoing = Take(
            written, IssueFrom(source, document, line, line.StockQuantity, postedAtUtc));

        return outgoing.IsFailure
            ? outgoing
            : Take(
                written,
                ReceiveInto(
                    destination, document, line, line.StockQuantity, unitCost, postedAtUtc));
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
            decimal unitCost = line.Rate > 0m ? line.Rate : positions.CostOf(line);

            return Take(
                written,
                ReceiveInto(
                    positions, document, line, line.StockQuantity, unitCost, postedAtUtc));
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
    /// <para>
    /// A batched line is counted against the batch rather than against the product.
    /// Somebody counting a shelf counts the cartons in front of them, which carry one
    /// batch number and one expiry date; comparing that figure against everything the
    /// product holds in the warehouse would report every other batch as missing.
    /// </para>
    /// </remarks>
    private static Result Count(
        List<StockLedgerEntry> written,
        PositionSet positions,
        StockDocument document,
        StockDocumentLine line,
        DateTimeOffset postedAtUtc)
    {
        decimal difference = line.StockQuantity - positions.QuantityOf(line);

        if (difference == 0m)
        {
            return Result.Success();
        }

        return difference > 0m
            ? Take(
                written,
                ReceiveInto(
                    positions, document, line, difference, positions.CostOf(line), postedAtUtc))
            : Take(written, IssueFrom(positions, document, line, -difference, postedAtUtc));
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
        List<BatchId> batchIds,
        IReadOnlyDictionary<BatchId, Batch> batches,
        CurrencyCode currency,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<ProductId, StockBalance> existing =
            await _balances.GetPositionsAsync(
                document.FirmId, warehouseId, productIds, cancellationToken);

        IReadOnlyDictionary<BatchId, BatchBalance> existingBatches = batchIds.Count == 0
            ? new Dictionary<BatchId, BatchBalance>()
            : await _batchBalances.GetPositionsAsync(
                document.FirmId, warehouseId, batchIds, cancellationToken);

        return new PositionSet(
            document,
            warehouseId,
            currency,
            new Dictionary<ProductId, StockBalance>(existing),
            new Dictionary<BatchId, BatchBalance>(existingBatches),
            batches,
            _balances,
            _batchBalances);
    }

    /// <summary>The positions of one warehouse, opening any that do not exist yet.</summary>
    private sealed class PositionSet
    {
        private readonly StockDocument _document;
        private readonly WarehouseId _warehouseId;
        private readonly CurrencyCode _currency;
        private readonly Dictionary<ProductId, StockBalance> _balances;
        private readonly Dictionary<BatchId, BatchBalance> _batchBalances;
        private readonly IReadOnlyDictionary<BatchId, Batch> _batches;
        private readonly IStockBalanceRepository _repository;
        private readonly IBatchBalanceRepository _batchRepository;

        internal PositionSet(
            StockDocument document,
            WarehouseId warehouseId,
            CurrencyCode currency,
            Dictionary<ProductId, StockBalance> balances,
            Dictionary<BatchId, BatchBalance> batchBalances,
            IReadOnlyDictionary<BatchId, Batch> batches,
            IStockBalanceRepository repository,
            IBatchBalanceRepository batchRepository)
        {
            _document = document;
            _warehouseId = warehouseId;
            _currency = currency;
            _balances = balances;
            _batchBalances = batchBalances;
            _batches = batches;
            _repository = repository;
            _batchRepository = batchRepository;
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

        internal BatchBalance ForBatch(Batch batch)
        {
            if (_batchBalances.TryGetValue(batch.Id, out BatchBalance? balance))
            {
                return balance;
            }

            BatchBalance opened = BatchBalance.Open(batch, _warehouseId, _currency);

            _batchRepository.Add(opened);
            _batchBalances[batch.Id] = opened;

            return opened;
        }

        /// <summary>The batch a line moves, or null where the product has none.</summary>
        /// <remarks>
        /// The dictionary is loaded from the identifiers on the lines themselves, so a
        /// miss cannot happen for a document that was assembled through the handler.
        /// It throws rather than returning null on a miss because the alternative -
        /// treating a batched line as unbatched - would move the product position
        /// without moving the batch's, which is precisely the drift this design exists
        /// to prevent.
        /// </remarks>
        internal Batch? BatchOf(StockDocumentLine line) =>
            line.BatchId is { } batchId ? _batches[batchId] : null;

        /// <summary>What one unit of what a line moves is currently carried at.</summary>
        internal decimal CostOf(StockDocumentLine line) =>
            BatchOf(line) is { } batch
                ? ForBatch(batch).UnitCost
                : For(line.ProductId).AverageCost;

        /// <summary>What is on hand of what a line moves, batch by batch where batched.</summary>
        internal decimal QuantityOf(StockDocumentLine line) =>
            BatchOf(line) is { } batch
                ? ForBatch(batch).Quantity
                : For(line.ProductId).Quantity;
    }
}
