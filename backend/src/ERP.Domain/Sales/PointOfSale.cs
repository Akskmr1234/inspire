using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Sales;

/// <summary>Identifies a cashier's shift at the point of sale.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PosShiftId(Guid Value) : IStronglyTypedId<PosShiftId>
{
    /// <inheritdoc />
    public static PosShiftId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PosShiftId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies the close of a trading day at the point of sale.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PosDayCloseId(Guid Value) : IStronglyTypedId<PosDayCloseId>
{
    /// <inheritdoc />
    public static PosDayCloseId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PosDayCloseId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One cashier's session at the counter: opened with the cash float in the drawer,
/// closed with what was counted in it.
/// </summary>
/// <remarks>
/// The sales themselves are ordinary sales invoices; the shift only brackets them, so the
/// close can say what the drawer should hold - the float, plus the cash taken, less the
/// cash refunded - and what was actually counted.
/// </remarks>
public sealed class PosShift : AggregateRoot<PosShiftId>, IFirmScoped
{
    /// <summary>The longest a closing note may be.</summary>
    public const int MaximumNotesLength = 500;

    private PosShift(
        PosShiftId id,
        TenantId tenantId,
        FirmId firmId,
        UserId userId,
        DateOnly businessDate,
        decimal openingCash,
        DateTimeOffset openedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        UserId = userId;
        BusinessDate = businessDate;
        OpeningCash = openingCash;
        OpenedAtUtc = openedAtUtc;
    }

    private PosShift()
    {
    }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the owning firm.</summary>
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the cashier.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the trading day it belongs to.</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Gets the cash float the drawer was opened with.</summary>
    public decimal OpeningCash { get; private set; }

    /// <summary>Gets when it was opened.</summary>
    public DateTimeOffset OpenedAtUtc { get; private set; }

    /// <summary>Gets when it was closed; null while it is open.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>Gets the cash the drawer should have held at the close.</summary>
    public decimal? ExpectedCash { get; private set; }

    /// <summary>Gets the cash counted in the drawer at the close.</summary>
    public decimal? CountedCash { get; private set; }

    /// <summary>Gets what the cashier noted at the close.</summary>
    public string? Notes { get; private set; }

    /// <summary>Gets a value indicating whether it is still open.</summary>
    public bool IsOpen => ClosedAtUtc is null;

    /// <summary>Opens a shift.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="userId">The cashier.</param>
    /// <param name="businessDate">The trading day.</param>
    /// <param name="openingCash">The float in the drawer.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The shift, or why it cannot be opened.</returns>
    public static Result<PosShift> Open(
        TenantId tenantId,
        FirmId firmId,
        UserId userId,
        DateOnly businessDate,
        decimal openingCash,
        DateTimeOffset nowUtc)
    {
        if (openingCash < 0m)
        {
            return Result.Failure<PosShift>(Error.Validation(
                "PosShift.NegativeFloat", "The opening cash cannot be negative."));
        }

        return Result.Success(new PosShift(
            PosShiftId.NewId(), tenantId, firmId, userId, businessDate, openingCash, nowUtc));
    }

    /// <summary>Closes the shift with what the drawer should hold and what was counted.</summary>
    /// <param name="expectedCash">The float plus the cash taken, less the cash refunded.</param>
    /// <param name="countedCash">What was counted.</param>
    /// <param name="notes">Anything the cashier wants to say about the difference.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or why it cannot be closed.</returns>
    public Result Close(decimal expectedCash, decimal countedCash, string? notes, DateTimeOffset nowUtc)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.BusinessRule(
                "PosShift.AlreadyClosed", "This shift is already closed."));
        }

        if (countedCash < 0m)
        {
            return Result.Failure(Error.Validation(
                "PosShift.NegativeCount", "The counted cash cannot be negative."));
        }

        string? note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (note is { Length: > MaximumNotesLength })
        {
            return Result.Failure(Error.Validation(
                "PosShift.NotesTooLong", $"A note is at most {MaximumNotesLength} characters."));
        }

        ExpectedCash = expectedCash;
        CountedCash = countedCash;
        Notes = note;
        ClosedAtUtc = nowUtc;

        return Result.Success();
    }
}

/// <summary>
/// The close of a trading day at the point of sale: the day's takings, by how they were
/// paid, frozen when the manager closed it. A closed day opens no more shifts.
/// </summary>
public sealed class PosDayClose : AggregateRoot<PosDayCloseId>, IFirmScoped
{
    /// <summary>The longest a closing note may be.</summary>
    public const int MaximumNotesLength = 500;

    private PosDayClose(
        PosDayCloseId id,
        TenantId tenantId,
        FirmId firmId,
        DateOnly businessDate,
        UserId closedBy,
        DateTimeOffset closedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BusinessDate = businessDate;
        ClosedBy = closedBy;
        ClosedAtUtc = closedAtUtc;
    }

    private PosDayClose()
    {
    }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the owning firm.</summary>
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the trading day.</summary>
    public DateOnly BusinessDate { get; private set; }

    /// <summary>Gets who closed it.</summary>
    public UserId ClosedBy { get; private set; }

    /// <summary>Gets when it was closed.</summary>
    public DateTimeOffset ClosedAtUtc { get; private set; }

    /// <summary>Gets how many sales the day made.</summary>
    public int SalesCount { get; private set; }

    /// <summary>Gets how many returns the day took back.</summary>
    public int ReturnsCount { get; private set; }

    /// <summary>Gets the day's sales, before returns.</summary>
    public decimal SalesTotal { get; private set; }

    /// <summary>Gets the day's returns.</summary>
    public decimal ReturnsTotal { get; private set; }

    /// <summary>Gets what came in as cash, net of cash refunds.</summary>
    public decimal CashTotal { get; private set; }

    /// <summary>Gets what came in by card, net of refunds.</summary>
    public decimal CardTotal { get; private set; }

    /// <summary>Gets what came in online (UPI and the rest), net of refunds.</summary>
    public decimal OnlineTotal { get; private set; }

    /// <summary>Gets what was sold on credit, net of returns.</summary>
    public decimal CreditTotal { get; private set; }

    /// <summary>Gets what the manager noted at the close.</summary>
    public string? Notes { get; private set; }

    /// <summary>Closes a day with its takings.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="businessDate">The trading day.</param>
    /// <param name="closedBy">Who closed it.</param>
    /// <param name="totals">The day's takings.</param>
    /// <param name="notes">Anything the manager wants to say.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The close, or why the day cannot be closed.</returns>
    public static Result<PosDayClose> Close(
        TenantId tenantId,
        FirmId firmId,
        DateOnly businessDate,
        UserId closedBy,
        PosTakings totals,
        string? notes,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(totals);

        string? note = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (note is { Length: > MaximumNotesLength })
        {
            return Result.Failure<PosDayClose>(Error.Validation(
                "PosDayClose.NotesTooLong", $"A note is at most {MaximumNotesLength} characters."));
        }

        return Result.Success(new PosDayClose(
            PosDayCloseId.NewId(), tenantId, firmId, businessDate, closedBy, nowUtc)
        {
            SalesCount = totals.SalesCount,
            ReturnsCount = totals.ReturnsCount,
            SalesTotal = totals.SalesTotal,
            ReturnsTotal = totals.ReturnsTotal,
            CashTotal = totals.CashTotal,
            CardTotal = totals.CardTotal,
            OnlineTotal = totals.OnlineTotal,
            CreditTotal = totals.CreditTotal,
            Notes = note,
        });
    }
}

/// <summary>What a shift or a day took, by how it was paid. Returns are taken off.</summary>
/// <param name="SalesCount">How many sales.</param>
/// <param name="ReturnsCount">How many returns.</param>
/// <param name="SalesTotal">The sales, before returns.</param>
/// <param name="ReturnsTotal">The returns.</param>
/// <param name="CashTotal">Cash taken, less cash refunded.</param>
/// <param name="CardTotal">Card, less card refunds.</param>
/// <param name="OnlineTotal">Online and UPI, less refunds.</param>
/// <param name="CreditTotal">On credit, less returns on credit.</param>
public sealed record PosTakings(
    int SalesCount,
    int ReturnsCount,
    decimal SalesTotal,
    decimal ReturnsTotal,
    decimal CashTotal,
    decimal CardTotal,
    decimal OnlineTotal,
    decimal CreditTotal)
{
    /// <summary>Gets nothing taken.</summary>
    public static PosTakings None { get; } = new(0, 0, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>Gets the sales less the returns.</summary>
    public decimal NetTotal => SalesTotal - ReturnsTotal;
}
