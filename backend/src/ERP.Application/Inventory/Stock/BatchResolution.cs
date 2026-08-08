using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Finds, opens, or generates the batch each line of a document moves.</summary>
/// <remarks>
/// <para>
/// Section 10 in one place. A line names its batch by identifier when the entry
/// screen has already offered a choice, by number when somebody is reading it off a
/// carton, or not at all - in which case a document that brings goods in generates
/// the next number for that product, and anything else is refused, because there is
/// nothing for it to take goods out of.
/// </para>
/// <para>
/// Resolved in three queries for the whole document rather than a few per line. A
/// receipt of forty batched products would otherwise be well over a hundred round
/// trips before it could refuse the first bad line.
/// </para>
/// </remarks>
internal static class BatchResolver
{
    /// <summary>Works out the batch behind every line, in the order they were given.</summary>
    /// <param name="document">The draft, for what its type is allowed to do.</param>
    /// <param name="request">The command as entered.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="context">Everything already loaded for the document.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batch per line, null where the product is not batched.</returns>
    internal static async Task<Result<IReadOnlyList<Batch?>>> ResolveAsync(
        StockDocument document,
        CreateStockDocumentCommand request,
        TenantId tenantId,
        StockContext context,
        IBatchRepository batches,
        CancellationToken cancellationToken)
    {
        Batch?[] resolved = new Batch?[request.Lines.Count];

        bool anyBatched = request.Lines.Any(line =>
            context.Products[ProductId.From(line.ProductId)].TracksBatches
            || line.BatchId is not null
            || !string.IsNullOrWhiteSpace(line.BatchNumber));

        // The common case, and worth its own exit: a firm that tracks no batches at
        // all should not pay three queries per document to be told so.
        if (!anyBatched)
        {
            return Result.Success<IReadOnlyList<Batch?>>(resolved);
        }

        IReadOnlyDictionary<BatchId, Batch> byId = await batches.GetManyAsync(
            [.. request.Lines
                .Where(line => line.BatchId is not null)
                .Select(line => BatchId.From(line.BatchId!.Value))
                .Distinct()],
            cancellationToken);

        // Keyed by product and number together, because a batch number is unique only
        // within its product: two suppliers both numbering their lots 001 is ordinary.
        Dictionary<(ProductId Product, string Number), Batch> byNumber = new(
            await batches.GetByNumbersAsync(
                document.FirmId,
                [.. request.Lines
                    .Where(line => line.BatchId is null
                        && !string.IsNullOrWhiteSpace(line.BatchNumber))
                    .Select(line => (
                        ProductId.From(line.ProductId), Normalise(line.BatchNumber!)))
                    .Distinct()],
                cancellationToken));

        Dictionary<ProductId, int> sequences = new(
            await batches.GetHighestAutoSequencesAsync(
                document.FirmId,
                [.. request.Lines
                    .Where(line => line.BatchId is null
                        && string.IsNullOrWhiteSpace(line.BatchNumber)
                        && context.Products[ProductId.From(line.ProductId)].TracksBatches)
                    .Select(line => ProductId.From(line.ProductId))
                    .Distinct()],
                cancellationToken));

        for (int index = 0; index < request.Lines.Count; index++)
        {
            StockDocumentLineInput line = request.Lines[index];
            Product product = context.Products[ProductId.From(line.ProductId)];

            Result<Batch?> batch = Resolve(
                document, line, product, tenantId, byId, byNumber, sequences, batches);

            if (batch.IsFailure)
            {
                return Result.Failure<IReadOnlyList<Batch?>>(batch.Error);
            }

            resolved[index] = batch.Value;
        }

        return Result.Success<IReadOnlyList<Batch?>>(resolved);
    }

    /// <summary>Loads the batches an already-entered document points at.</summary>
    /// <param name="document">The document.</param>
    /// <param name="batches">The batch repository.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batches its lines name, by identifier.</returns>
    internal static async Task<IReadOnlyDictionary<BatchId, Batch>> ForDocumentAsync(
        StockDocument document,
        IBatchRepository batches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<BatchId> ids =
            [.. document.Lines
                .Where(line => line.BatchId is not null)
                .Select(line => line.BatchId!.Value)
                .Distinct()];

        return ids.Count == 0
            ? new Dictionary<BatchId, Batch>()
            : await batches.GetManyAsync(ids, cancellationToken);
    }

    private static Result<Batch?> Resolve(
        StockDocument document,
        StockDocumentLineInput line,
        Product product,
        TenantId tenantId,
        IReadOnlyDictionary<BatchId, Batch> byId,
        Dictionary<(ProductId Product, string Number), Batch> byNumber,
        Dictionary<ProductId, int> sequences,
        IBatchRepository batches)
    {
        bool named = line.BatchId is not null || !string.IsNullOrWhiteSpace(line.BatchNumber);

        // Refused here as well as by the document, because the document is only told
        // about batches it is given: a batch offered for a product that is not tracked
        // in batches would otherwise be dropped without anybody being told.
        if (!product.TracksBatches)
        {
            return named
                ? Result.Failure<Batch?>(Error.Validation(
                    "StockDocument.BatchNotTracked",
                    $"'{product.Code}' is not tracked in batches, so a batch cannot be "
                    + "given for it."))
                : Result.Success<Batch?>(null);
        }

        if (line.BatchId is { } id)
        {
            if (!byId.TryGetValue(BatchId.From(id), out Batch? chosen)
                || chosen.FirmId != document.FirmId
                || chosen.ProductId != product.Id)
            {
                return Result.Failure<Batch?>(Error.NotFound(
                    "Batch.NotFound",
                    $"No such batch of '{product.Code}' in the selected firm."));
            }

            Result agree = EnsureDatesAgree(line, chosen);

            return agree.IsFailure
                ? Result.Failure<Batch?>(agree.Error)
                : Result.Success<Batch?>(Costed(chosen, document, line));
        }

        if (!string.IsNullOrWhiteSpace(line.BatchNumber))
        {
            string number = Normalise(line.BatchNumber);

            if (byNumber.TryGetValue((product.Id, number), out Batch? existing))
            {
                Result agree = EnsureDatesAgree(line, existing);

                return agree.IsFailure
                    ? Result.Failure<Batch?>(agree.Error)
                    : Result.Success<Batch?>(Costed(existing, document, line));
            }

            return document.OpensBatches
                ? Open(document, product, tenantId, number, null, line, byNumber, batches)
                : Result.Failure<Batch?>(Error.NotFound(
                    "Batch.NotFound",
                    $"'{product.Code}' has no batch '{number}'. A {document.Type} moves "
                    + "goods that are already in stock, so it cannot open one."));
        }

        // Nothing named at all. Only a document bringing goods in from outside may
        // invent the batch and its number both; anything else has to be told which
        // goods it is moving, because the goods already exist and so does their lot.
        if (!document.GeneratesBatchNumbers || line.Quantity <= 0m)
        {
            return Result.Failure<Batch?>(Error.Validation(
                "StockDocument.BatchRequired",
                $"'{product.Code}' is tracked in batches, so the line must say which "
                + "batch moved."));
        }

        sequences.TryGetValue(product.Id, out int highest);

        Result<(int Sequence, string Number)> generated = Batch.NextNumber(highest);

        if (generated.IsFailure)
        {
            return Result.Failure<Batch?>(generated.Error);
        }

        sequences[product.Id] = generated.Value.Sequence;

        return Open(
            document, product, tenantId, generated.Value.Number, generated.Value.Sequence,
            line, byNumber, batches);
    }

    private static Result<Batch?> Open(
        StockDocument document,
        Product product,
        TenantId tenantId,
        string number,
        int? sequence,
        StockDocumentLineInput line,
        Dictionary<(ProductId Product, string Number), Batch> byNumber,
        IBatchRepository batches)
    {
        Result<Batch> opened = Batch.Open(
            tenantId,
            document.FirmId,
            product,
            number,
            line.ManufacturedOn,
            line.ExpiresOn,
            document.CarriesRate ? line.Rate : 0m,
            sequence);

        if (opened.IsFailure)
        {
            return Result.Failure<Batch?>(opened.Error);
        }

        batches.Add(opened.Value);

        // Held so that a second line naming the same new number on the same document
        // finds the batch this one opened rather than opening a second of it - which
        // the unique index would refuse at save time, with a message about an index.
        byNumber[(product.Id, number)] = opened.Value;

        return Result.Success<Batch?>(opened.Value);
    }

    /// <summary>Fills in what a batch was bought at, the first time a receipt says.</summary>
    private static Batch Costed(Batch batch, StockDocument document, StockDocumentLineInput line)
    {
        if (document.CarriesRate && line.Rate > 0m)
        {
            batch.RecordPurchaseRate(line.Rate);
        }

        return batch;
    }

    /// <summary>Refuses a line whose dates disagree with the batch it names.</summary>
    /// <remarks>
    /// The entry screen fills these in from the batch once it is chosen, so a
    /// difference means somebody typed over them. Keeping the batch's dates silently
    /// would ignore what they entered; taking theirs silently would restate the expiry
    /// of goods already on the shelf from a line of an unrelated document. Correcting
    /// an expiry date is its own operation, and this says so.
    /// </remarks>
    private static Result EnsureDatesAgree(StockDocumentLineInput line, Batch batch) =>
        (line.ManufacturedOn is not null && line.ManufacturedOn != batch.ManufacturedOn)
        || (line.ExpiresOn is not null && line.ExpiresOn != batch.ExpiresOn)
            ? Result.Failure(Error.BusinessRule(
                "Batch.DatesDiffer",
                $"Batch '{batch.Number}' is already on file with different dates. "
                + "Correct the batch itself rather than restating them here."))
            : Result.Success();

    private static string Normalise(string number) => number.Trim().ToUpperInvariant();
}
