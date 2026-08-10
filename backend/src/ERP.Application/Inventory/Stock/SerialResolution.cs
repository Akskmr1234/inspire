using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Stock;

/// <summary>Finds or writes down the units each line of a document names.</summary>
/// <remarks>
/// <para>
/// Section 12.7's two halves. Goods coming in bring numbers nobody has seen before, so
/// those are written down against the document that brings them; goods going out must
/// name units that are already on the shelf, and a number the firm does not hold is a
/// mistake rather than a new unit.
/// </para>
/// <para>
/// The units are recorded rather than taken into stock. The document may still be a
/// draft, and a draft moves nothing - offering its units for sale would be the serial
/// equivalent of a draft receipt raising a stock position. Posting is what makes them
/// real, and that happens in <see cref="StockPoster"/>.
/// </para>
/// </remarks>
internal static class SerialResolver
{
    /// <summary>Works out the units behind every line, in the order they were given.</summary>
    /// <param name="document">The draft, for what its type is allowed to do.</param>
    /// <param name="request">The command as entered.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="context">Everything already loaded for the document.</param>
    /// <param name="batches">The batch each line moves, by line position.</param>
    /// <param name="serials">The serial repository.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units per line, empty where the product is not serialised.</returns>
    internal static async Task<Result<IReadOnlyList<IReadOnlyList<SerialNumber>>>> ResolveAsync(
        StockDocument document,
        CreateStockDocumentCommand request,
        TenantId tenantId,
        StockContext context,
        IReadOnlyList<Batch?> batches,
        ISerialNumberRepository serials,
        CancellationToken cancellationToken)
    {
        List<IReadOnlyList<SerialNumber>> resolved = [];

        bool anySerialised = request.Lines.Any(line =>
            context.Products[ProductId.From(line.ProductId)].TracksSerialNumbers
            || line.SerialNumbers is { Count: > 0 });

        // The common case pays nothing. Most firms serialise a handful of products and
        // most documents name none of them.
        if (!anySerialised)
        {
            for (int index = 0; index < request.Lines.Count; index++)
            {
                resolved.Add([]);
            }

            return Result.Success<IReadOnlyList<IReadOnlyList<SerialNumber>>>(resolved);
        }

        Dictionary<(ProductId Product, string Number), SerialNumber> known = new(
            await serials.GetByNumbersAsync(
                document.FirmId,
                [.. request.Lines
                    .SelectMany(line => (line.SerialNumbers ?? [])
                        .Select(number => (
                            ProductId.From(line.ProductId), Normalise(number))))
                    .Distinct()],
                cancellationToken));

        for (int index = 0; index < request.Lines.Count; index++)
        {
            StockDocumentLineInput line = request.Lines[index];
            Product product = context.Products[ProductId.From(line.ProductId)];

            Result<IReadOnlyList<SerialNumber>> units = Resolve(
                document, line, product, tenantId, batches[index], known, serials);

            if (units.IsFailure)
            {
                return Result.Failure<IReadOnlyList<IReadOnlyList<SerialNumber>>>(units.Error);
            }

            resolved.Add(units.Value);
        }

        return Result.Success<IReadOnlyList<IReadOnlyList<SerialNumber>>>(resolved);
    }

    private static Result<IReadOnlyList<SerialNumber>> Resolve(
        StockDocument document,
        StockDocumentLineInput line,
        Product product,
        TenantId tenantId,
        Batch? batch,
        Dictionary<(ProductId Product, string Number), SerialNumber> known,
        ISerialNumberRepository serials)
    {
        IReadOnlyList<string> numbers = line.SerialNumbers ?? [];

        if (!product.TracksSerialNumbers)
        {
            return numbers.Count > 0
                ? Result.Failure<IReadOnlyList<SerialNumber>>(Error.Validation(
                    "StockDocument.SerialsNotTracked",
                    $"'{product.Code}' is not tracked by serial number, so units cannot be "
                    + "named for it."))
                : Result.Success<IReadOnlyList<SerialNumber>>([]);
        }

        List<SerialNumber> units = [];

        foreach (string entered in numbers)
        {
            if (string.IsNullOrWhiteSpace(entered))
            {
                return Result.Failure<IReadOnlyList<SerialNumber>>(Error.Validation(
                    "Serial.NumberRequired",
                    $"A blank serial number was given for '{product.Code}'."));
            }

            string number = Normalise(entered);

            if (known.TryGetValue((product.Id, number), out SerialNumber? existing))
            {
                Result usable = EnsureUsable(document, existing, product);

                if (usable.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<SerialNumber>>(usable.Error);
                }

                units.Add(existing);
                continue;
            }

            // Not on file. Only a document bringing goods in may write a unit down;
            // anything else is moving goods that are already here, so an unknown number
            // is a number somebody has mistyped.
            if (!document.OpensBatches)
            {
                return Result.Failure<IReadOnlyList<SerialNumber>>(Error.NotFound(
                    "Serial.NotFound",
                    $"'{product.Code}' has no unit numbered '{number}'. A {document.Type} "
                    + "moves units that are already in stock."));
            }

            Result<SerialNumber> opened = SerialNumber.Receive(
                tenantId,
                document.FirmId,
                product,
                number,
                document.WarehouseId,
                document.Date,
                document.Id,
                document.CarriesRate ? line.Rate : 0m,
                batch,
                line.WarrantyUntil);

            if (opened.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SerialNumber>>(opened.Error);
            }

            serials.Add(opened.Value);

            // Held so a second line naming the same new number finds this unit rather
            // than writing a second row the unique index would refuse at save time.
            known[(product.Id, number)] = opened.Value;
            units.Add(opened.Value);
        }

        return Result.Success<IReadOnlyList<SerialNumber>>(units);
    }

    /// <summary>Checks that a unit already on file can do what the document asks.</summary>
    /// <remarks>
    /// A unit written down by a draft that is still a draft can be named again - it is
    /// the same goods arriving, entered twice by somebody correcting themselves. What
    /// cannot happen is a document taking out a unit that is not on a shelf, which is
    /// the promise section 12.7 makes about sold serials never reappearing; the
    /// aggregate refuses that at the moment of the movement, and this refuses it early
    /// so the message names the document rather than the transition.
    /// </remarks>
    private static Result EnsureUsable(
        StockDocument document,
        SerialNumber serial,
        Product product)
    {
        if (serial.FirmId != document.FirmId || serial.ProductId != product.Id)
        {
            return Result.Failure(Error.NotFound(
                "Serial.NotFound",
                $"'{product.Code}' has no unit numbered '{serial.Number}'."));
        }

        if (document.OpensBatches)
        {
            return serial.Status is SerialStatus.Recorded or SerialStatus.Issued
                ? Result.Success()
                : Result.Failure(Error.BusinessRule(
                    "Serial.AlreadyInStock",
                    $"Unit '{serial.Number}' is already in stock, so it cannot be received "
                    + "again."));
        }

        return serial.IsAvailable
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "Serial.NotAvailable",
                $"Unit '{serial.Number}' is not on a shelf, so it cannot go out."));
    }

    private static string Normalise(string number) => number.Trim().ToUpperInvariant();
}
