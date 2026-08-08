using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>
/// One lot of one product: what it is called, when it expires, and what it cost to buy.
/// </summary>
/// <remarks>
/// <para>
/// Section 10. A batch is a fact about goods rather than about a place, so it is
/// identified per product and per firm - one batch number, one expiry date, one
/// purchase rate, wherever the goods happen to be sitting. How much of it is in each
/// warehouse is a different question, answered by <see cref="BatchBalance"/>, for the
/// same reason the product's own position is per warehouse: quantity is a fact about
/// a shelf.
/// </para>
/// <para>
/// The batch carries the rate it arrived at, which is what the sales screen shows
/// against each batch and what the specification means by "profit always uses actual
/// batch cost". That is not the same figure as the cost a warehouse now carries it
/// at: a second delivery of the same batch at a different price averages into the
/// position but does not restate what the first delivery cost. Both are kept, because
/// they answer different questions and neither can be derived from the other.
/// </para>
/// </remarks>
public sealed class Batch : AggregateRoot<BatchId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a batch number may be.</summary>
    /// <remarks>
    /// Generous, because supplier batch numbers are copied off a carton rather than
    /// invented here, and a pharmaceutical lot code with a plant and a line in it runs
    /// well past what a tidier limit would allow.
    /// </remarks>
    public const int MaximumNumberLength = 40;

    /// <summary>The highest sequence an auto-generated number can express.</summary>
    /// <remarks>
    /// Twenty-six letters of a thousand each. A product that gets past 26,000
    /// generated batches has outgrown the format the specification asked for, and
    /// silently rolling over to something else would produce a second A001.
    /// </remarks>
    public const int MaximumAutoSequence = 26 * 999;

    private Batch(
        BatchId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        string number,
        int? autoSequence,
        DateOnly? manufacturedOn,
        DateOnly? expiresOn,
        decimal purchaseRate)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        Number = number;
        AutoSequence = autoSequence;
        ManufacturedOn = manufacturedOn;
        ExpiresOn = expiresOn;
        PurchaseRate = purchaseRate;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Batch() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product this is a batch of.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the batch number, unique within the product.</summary>
    /// <remarks>
    /// Within the product rather than within the firm. Two suppliers both numbering
    /// their lots from 001 is the normal case, and forcing those onto one sequence
    /// would mean the number on the carton is not the number in the system.
    /// </remarks>
    public string Number { get; private set; }

    /// <summary>Gets the place this number occupies in the generated sequence.</summary>
    /// <remarks>
    /// Null for a number that is not in the generated format at all. Kept so
    /// generation continues from the last number in that format rather than from
    /// whatever sorts highest - a supplier lot code of "Z9" would otherwise make the
    /// next generated number unguessable, and a batch called "0001-A" impossible.
    /// <para>
    /// Filled in for a number somebody typed as well, when what they typed happens to
    /// look like a generated one. A storekeeper who enters <c>A004</c> by hand has
    /// used that place in the sequence whether or not the system issued it, and
    /// generation that ignored them would offer <c>A004</c> again and be refused by
    /// the unique index with nothing useful to say.
    /// </para>
    /// </remarks>
    public int? AutoSequence { get; private set; }

    /// <summary>Gets the date the goods were produced.</summary>
    public DateOnly? ManufacturedOn { get; private set; }

    /// <summary>Gets the last date the goods may be used.</summary>
    /// <remarks>
    /// Inclusive: a batch marked as expiring on the 30th is good on the 30th. That is
    /// how the date is printed on the carton, and reading it any other way would put
    /// the expiry report a day out from the goods it describes.
    /// </remarks>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Gets what one stock unit of this batch was bought at.</summary>
    public decimal PurchaseRate { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether the number is one the generated sequence can issue.</summary>
    public bool IsSequenced => AutoSequence is not null;

    /// <summary>Opens a batch of a product.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="product">The product, which must be tracked in batches.</param>
    /// <param name="number">The batch number.</param>
    /// <param name="manufacturedOn">When the goods were produced.</param>
    /// <param name="expiresOn">When they expire. Derived from the shelf life if omitted.</param>
    /// <param name="purchaseRate">What one stock unit was bought at.</param>
    /// <param name="autoSequence">The sequence position, when the number was generated.</param>
    /// <returns>The batch, or the reason it was refused.</returns>
    public static Result<Batch> Open(
        TenantId tenantId,
        FirmId firmId,
        Product product,
        string number,
        DateOnly? manufacturedOn = null,
        DateOnly? expiresOn = null,
        decimal purchaseRate = 0m,
        int? autoSequence = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        // Refused rather than tolerated. A batch of a product nobody asked to track in
        // batches would be a lot that the sales screen never offers and the position
        // never consults - stock recorded twice, in two places, one of them invisible.
        if (!product.TracksBatches)
        {
            return Result.Failure<Batch>(Error.BusinessRule(
                "Batch.NotTracked",
                $"'{product.Code}' is not tracked in batches. Turn batch tracking on "
                + "for the product before recording batches of it."));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<Batch>(Error.Validation(
                "Batch.NumberRequired", "A batch number is required."));
        }

        string trimmed = number.Trim().ToUpperInvariant();

        if (trimmed.Length > MaximumNumberLength)
        {
            return Result.Failure<Batch>(Error.Validation(
                "Batch.NumberTooLong",
                $"A batch number cannot exceed {MaximumNumberLength} characters."));
        }

        if (purchaseRate < 0m)
        {
            return Result.Failure<Batch>(Error.Validation(
                "Batch.RateNegative", "A batch cannot have been bought at a negative rate."));
        }

        // Derived only where nothing was given. A supplier's printed expiry date beats
        // this arithmetic every time, and the shelf life is a default for the goods a
        // firm produces itself rather than a rule about the ones it buys.
        DateOnly? expiry = expiresOn
            ?? (manufacturedOn is { } made && product.ShelfLifeDays is { } days
                ? made.AddDays(days)
                : null);

        Result dated = EnsureOrdered(manufacturedOn, expiry);

        return dated.IsFailure
            ? Result.Failure<Batch>(dated.Error)
            : Result.Success(new Batch(
                BatchId.NewId(),
                tenantId,
                firmId,
                product.Id,
                trimmed,
                autoSequence ?? SequenceOf(trimmed),
                manufacturedOn,
                expiry,
                purchaseRate));
    }

    /// <summary>The number the next generated batch of a product carries.</summary>
    /// <param name="highestSequence">The highest sequence already issued, or null for none.</param>
    /// <returns>The sequence and the number it formats to, or the reason there is none left.</returns>
    /// <remarks>
    /// <c>A001</c>, <c>A002</c>, and on to <c>A999</c> before <c>B001</c>, which is the
    /// format section 10 asks for. It is per product, so two products both reach
    /// <c>A001</c> and neither is confused with the other.
    /// <para>
    /// Generated from the last sequence this system issued rather than from the
    /// highest number on file. A batch number typed off a carton is any string a
    /// supplier likes, and continuing a sequence from one of those is not a question
    /// with an answer.
    /// </para>
    /// </remarks>
    public static Result<(int Sequence, string Number)> NextNumber(int? highestSequence)
    {
        int next = (highestSequence ?? 0) + 1;

        if (next is < 1 or > MaximumAutoSequence)
        {
            return Result.Failure<(int, string)>(Error.BusinessRule(
                "Batch.SequenceExhausted",
                $"This product has used all {MaximumAutoSequence} generated batch "
                + "numbers. Enter the batch number by hand."));
        }

        return Result.Success((next, FormatNumber(next)));
    }

    /// <summary>Formats a sequence position as a batch number.</summary>
    /// <param name="sequence">The position, from one.</param>
    /// <returns>The number, such as <c>A001</c>.</returns>
    public static string FormatNumber(int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaximumAutoSequence);

        int zeroBased = sequence - 1;
        char letter = (char)('A' + (zeroBased / 999));
        int within = (zeroBased % 999) + 1;

        return $"{letter}{within:000}";
    }

    /// <summary>Reads back the sequence position a number in the generated format holds.</summary>
    /// <param name="number">The batch number, already trimmed and upper-cased.</param>
    /// <returns>The position, or null when the number is not in that format.</returns>
    public static int? SequenceOf(string number)
    {
        if (number is not { Length: 4 }
            || number[0] is < 'A' or > 'Z'
            || !char.IsAsciiDigit(number[1])
            || !char.IsAsciiDigit(number[2])
            || !char.IsAsciiDigit(number[3]))
        {
            return null;
        }

        // Digit by digit rather than by parsing, so that nothing a number parser is
        // lenient about - a sign, surrounding space, a different script's digits -
        // maps onto a place in the sequence that the generator would then issue again.
        int within = ((number[1] - '0') * 100) + ((number[2] - '0') * 10) + (number[3] - '0');

        return within == 0 ? null : ((number[0] - 'A') * 999) + within;
    }

    /// <summary>Corrects the dates on the batch.</summary>
    /// <param name="manufacturedOn">When the goods were produced.</param>
    /// <param name="expiresOn">When they expire.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Correctable after the fact, unlike most of what a posted document touches. An
    /// expiry date is transcribed off a carton by somebody in a hurry, the error is
    /// found when the goods are picked, and the alternative to correcting it is
    /// writing the batch off and receiving it again - which moves stock that never
    /// moved to fix a typing mistake.
    /// </remarks>
    public Result SetDates(DateOnly? manufacturedOn, DateOnly? expiresOn)
    {
        Result dated = EnsureOrdered(manufacturedOn, expiresOn);

        if (dated.IsFailure)
        {
            return dated;
        }

        ManufacturedOn = manufacturedOn;
        ExpiresOn = expiresOn;

        return Result.Success();
    }

    /// <summary>Records what the batch was bought at, if it was not known before.</summary>
    /// <param name="purchaseRate">The rate one stock unit was bought at.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Only fills a rate of nothing. A batch that already carries what it cost keeps
    /// that figure: a later receipt into the same batch at a different price is a
    /// change to what the warehouse carries it at, not a change to what the first
    /// delivery was bought for, and letting the second overwrite the first would
    /// restate the margin on everything already sold out of it.
    /// </remarks>
    public Result RecordPurchaseRate(decimal purchaseRate)
    {
        if (purchaseRate < 0m)
        {
            return Result.Failure(Error.Validation(
                "Batch.RateNegative", "A batch cannot have been bought at a negative rate."));
        }

        if (PurchaseRate == 0m)
        {
            PurchaseRate = purchaseRate;
        }

        return Result.Success();
    }

    /// <summary>Says whether the batch has expired by a given date.</summary>
    /// <param name="on">The date to judge it on.</param>
    /// <returns><see langword="true"/> if the goods are past their expiry date.</returns>
    /// <remarks>
    /// A batch with no expiry date never expires. That is the honest reading of an
    /// empty field: most goods do not expire, and treating a blank as "expired today"
    /// would put every one of them on the expiry report.
    /// </remarks>
    public bool HasExpiredBy(DateOnly on) => ExpiresOn is { } expiry && expiry < on;

    private static Result EnsureOrdered(DateOnly? manufacturedOn, DateOnly? expiresOn) =>
        manufacturedOn is { } made && expiresOn is { } expiry && expiry < made
            ? Result.Failure(Error.Validation(
                "Batch.ExpiryBeforeManufacture",
                "A batch cannot expire before it was made."))
            : Result.Success();
}
