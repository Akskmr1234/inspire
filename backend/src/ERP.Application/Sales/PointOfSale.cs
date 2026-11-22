using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>Where the counter stands for the signed-in cashier: their shift, and the day.</summary>
public sealed record GetPosStatusQuery : IQuery<PosStatusResponse>;

/// <summary>Opens the signed-in cashier's shift with the float in the drawer.</summary>
/// <param name="OpeningCash">The cash float.</param>
public sealed record OpenPosShiftCommand(decimal OpeningCash) : ICommand<PosShiftView>, ITransactional;

/// <summary>Closes a shift with the cash counted in the drawer.</summary>
/// <param name="ShiftId">The shift.</param>
/// <param name="CountedCash">What was counted.</param>
/// <param name="Notes">Anything to say about a difference.</param>
public sealed record ClosePosShiftCommand(Guid ShiftId, decimal CountedCash, string? Notes = null)
    : ICommand<PosShiftView>, ITransactional;

/// <summary>A day's takings at the counter, and whether it is closed.</summary>
/// <param name="Date">The day; today when omitted.</param>
public sealed record GetPosDayQuery(DateOnly? Date = null) : IQuery<PosDayView>;

/// <summary>Closes a trading day: no shift may still be open, and none opens after.</summary>
/// <param name="Date">The day; today when omitted.</param>
/// <param name="Notes">Anything the manager wants to say.</param>
public sealed record ClosePosDayCommand(DateOnly? Date = null, string? Notes = null)
    : ICommand<PosDayView>, ITransactional;

/// <summary>A shift as the counter shows it.</summary>
/// <param name="ShiftId">The shift.</param>
/// <param name="BusinessDate">Its trading day.</param>
/// <param name="OpenedAtUtc">When it opened.</param>
/// <param name="OpeningCash">The float.</param>
/// <param name="ClosedAtUtc">When it closed; null while open.</param>
/// <param name="ExpectedCash">What the drawer should hold: the float, plus cash taken, less cash refunded.</param>
/// <param name="CountedCash">What was counted; null while open.</param>
/// <param name="Notes">The cashier's note.</param>
/// <param name="Takings">What the shift took, by how it was paid.</param>
public sealed record PosShiftView(
    Guid ShiftId,
    DateOnly BusinessDate,
    DateTimeOffset OpenedAtUtc,
    decimal OpeningCash,
    DateTimeOffset? ClosedAtUtc,
    decimal ExpectedCash,
    decimal? CountedCash,
    string? Notes,
    PosTakings Takings);

/// <summary>A day at the counter.</summary>
/// <param name="Date">The day.</param>
/// <param name="Takings">What it took, by how it was paid.</param>
/// <param name="OpenShifts">How many shifts are still open on it.</param>
/// <param name="IsClosed">Whether it has been closed.</param>
/// <param name="ClosedAtUtc">When it was closed.</param>
/// <param name="Notes">The manager's note at the close.</param>
public sealed record PosDayView(
    DateOnly Date,
    PosTakings Takings,
    int OpenShifts,
    bool IsClosed,
    DateTimeOffset? ClosedAtUtc,
    string? Notes);

/// <summary>Where the counter stands.</summary>
/// <param name="Today">The firm's trading day.</param>
/// <param name="Shift">The cashier's open shift, or null.</param>
/// <param name="DayClosed">Whether today has been closed.</param>
public sealed record PosStatusResponse(DateOnly Today, PosShiftView? Shift, bool DayClosed);

/// <summary>Shifts and day closes, and the takings they are measured by.</summary>
public interface IPosStore
{
    /// <summary>The cashier's open shift in a firm, if any.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="userId">The cashier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shift, or null.</returns>
    Task<PosShift?> FindOpenShiftAsync(FirmId firmId, UserId userId, CancellationToken cancellationToken = default);

    /// <summary>A shift by its id.</summary>
    /// <param name="id">The shift.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shift, or null.</returns>
    Task<PosShift?> FindShiftAsync(PosShiftId id, CancellationToken cancellationToken = default);

    /// <summary>How many shifts are still open on a day.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="date">The day.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The count.</returns>
    Task<int> CountOpenShiftsAsync(FirmId firmId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>A day's close, if it has been closed.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="date">The day.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The close, or null.</returns>
    Task<PosDayClose?> FindDayCloseAsync(FirmId firmId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>What posted sales took, for a cashier over a time span or for a whole day.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="date">A whole trading day; null to go by the cashier and span.</param>
    /// <param name="userId">The cashier who entered them; null for everyone.</param>
    /// <param name="fromUtc">Entered at or after; null for no lower bound.</param>
    /// <param name="toUtc">Entered at or before; null for no upper bound.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The takings.</returns>
    Task<PosTakings> TakingsAsync(
        FirmId firmId,
        DateOnly? date,
        UserId? userId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a shift.</summary>
    /// <param name="shift">The shift.</param>
    void Add(PosShift shift);

    /// <summary>Adds a day close.</summary>
    /// <param name="close">The close.</param>
    void Add(PosDayClose close);
}

/// <summary>Handles the counter's shifts and day close.</summary>
public sealed class PosHandlers
    : IQueryHandler<GetPosStatusQuery, PosStatusResponse>,
      IQueryHandler<GetPosDayQuery, PosDayView>,
      ICommandHandler<OpenPosShiftCommand, PosShiftView>,
      ICommandHandler<ClosePosShiftCommand, PosShiftView>,
      ICommandHandler<ClosePosDayCommand, PosDayView>
{
    private readonly IPosStore _store;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="PosHandlers"/> class.</summary>
    /// <param name="store">The shift and day-close store.</param>
    /// <param name="firms">The firm repository, for the firm's time zone.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="currentUser">The signed-in cashier.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PosHandlers(
        IPosStore store,
        IFirmRepository firms,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _store = store;
        _firms = firms;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PosStatusResponse>> Handle(GetPosStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<(FirmId Firm, DateOnly Today)> scope = await ScopeAsync(cancellationToken);

        if (scope.IsFailure)
        {
            return Result.Failure<PosStatusResponse>(scope.Error);
        }

        (FirmId firmId, DateOnly today) = scope.Value;
        PosShift? shift = await _store.FindOpenShiftAsync(firmId, _currentUser.UserId, cancellationToken);
        PosDayClose? closed = await _store.FindDayCloseAsync(firmId, today, cancellationToken);

        return Result.Success(new PosStatusResponse(
            today,
            shift is null ? null : await ViewAsync(shift, cancellationToken),
            closed is not null));
    }

    /// <inheritdoc />
    public async Task<Result<PosDayView>> Handle(GetPosDayQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<(FirmId Firm, DateOnly Today)> scope = await ScopeAsync(cancellationToken);

        return scope.IsFailure
            ? Result.Failure<PosDayView>(scope.Error)
            : Result.Success(await DayAsync(scope.Value.Firm, request.Date ?? scope.Value.Today, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PosShiftView>> Handle(OpenPosShiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<(FirmId Firm, DateOnly Today)> scope = await ScopeAsync(cancellationToken);

        if (scope.IsFailure)
        {
            return Result.Failure<PosShiftView>(scope.Error);
        }

        (FirmId firmId, DateOnly today) = scope.Value;

        if (await _store.FindDayCloseAsync(firmId, today, cancellationToken) is not null)
        {
            return Result.Failure<PosShiftView>(Error.BusinessRule(
                "Pos.DayClosed", "Today has been closed; no more shifts open on it."));
        }

        if (await _store.FindOpenShiftAsync(firmId, _currentUser.UserId, cancellationToken) is not null)
        {
            return Result.Failure<PosShiftView>(Error.Conflict(
                "Pos.ShiftAlreadyOpen", "You already have a shift open. Close it before opening another."));
        }

        Result<PosShift> opened = PosShift.Open(
            _tenantContext.TenantId, firmId, _currentUser.UserId, today, request.OpeningCash, _clock.UtcNow);

        if (opened.IsFailure)
        {
            return Result.Failure<PosShiftView>(opened.Error);
        }

        _store.Add(opened.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ViewAsync(opened.Value, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PosShiftView>> Handle(ClosePosShiftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<PosShiftView>(NoFirm());
        }

        PosShift? shift = await _store.FindShiftAsync(PosShiftId.From(request.ShiftId), cancellationToken);

        // A cashier closes their own shift; anyone else's is not theirs to count.
        if (shift is null || shift.FirmId != firmId || shift.UserId != _currentUser.UserId)
        {
            return Result.Failure<PosShiftView>(Error.NotFound(
                "Pos.ShiftNotFound", "No such open shift of yours."));
        }

        PosShiftView open = await ViewAsync(shift, cancellationToken);
        Result closed = shift.Close(open.ExpectedCash, request.CountedCash, request.Notes, _clock.UtcNow);

        if (closed.IsFailure)
        {
            return Result.Failure<PosShiftView>(closed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ViewAsync(shift, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PosDayView>> Handle(ClosePosDayCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<(FirmId Firm, DateOnly Today)> scope = await ScopeAsync(cancellationToken);

        if (scope.IsFailure)
        {
            return Result.Failure<PosDayView>(scope.Error);
        }

        FirmId firmId = scope.Value.Firm;
        DateOnly date = request.Date ?? scope.Value.Today;

        if (date > scope.Value.Today)
        {
            return Result.Failure<PosDayView>(Error.Validation(
                "Pos.DayInFuture", "A day that has not started cannot be closed."));
        }

        if (await _store.FindDayCloseAsync(firmId, date, cancellationToken) is not null)
        {
            return Result.Failure<PosDayView>(Error.Conflict(
                "Pos.DayAlreadyClosed", "That day is already closed."));
        }

        int open = await _store.CountOpenShiftsAsync(firmId, date, cancellationToken);

        if (open > 0)
        {
            return Result.Failure<PosDayView>(Error.BusinessRule(
                "Pos.ShiftsStillOpen",
                $"{open} shift(s) are still open. Every cashier closes their shift before the day is closed."));
        }

        PosTakings takings = await _store.TakingsAsync(firmId, date, null, null, null, cancellationToken);
        Result<PosDayClose> closed = PosDayClose.Close(
            _tenantContext.TenantId, firmId, date, _currentUser.UserId, takings, request.Notes, _clock.UtcNow);

        if (closed.IsFailure)
        {
            return Result.Failure<PosDayView>(closed.Error);
        }

        _store.Add(closed.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await DayAsync(firmId, date, cancellationToken));
    }

    private static Error NoFirm() =>
        Error.BusinessRule("Firm.NotSelected", "Select a firm before using the point of sale.");

    private async Task<PosDayView> DayAsync(FirmId firmId, DateOnly date, CancellationToken cancellationToken)
    {
        PosDayClose? closed = await _store.FindDayCloseAsync(firmId, date, cancellationToken);
        PosTakings takings = await _store.TakingsAsync(firmId, date, null, null, null, cancellationToken);

        return new PosDayView(
            date,
            takings,
            await _store.CountOpenShiftsAsync(firmId, date, cancellationToken),
            closed is not null,
            closed?.ClosedAtUtc,
            closed?.Notes);
    }

    private async Task<PosShiftView> ViewAsync(PosShift shift, CancellationToken cancellationToken)
    {
        PosTakings takings = await _store.TakingsAsync(
            shift.FirmId, null, shift.UserId, shift.OpenedAtUtc, shift.ClosedAtUtc, cancellationToken);

        return new PosShiftView(
            shift.Id.Value,
            shift.BusinessDate,
            shift.OpenedAtUtc,
            shift.OpeningCash,
            shift.ClosedAtUtc,
            shift.ExpectedCash ?? shift.OpeningCash + takings.CashTotal,
            shift.CountedCash,
            shift.Notes,
            takings);
    }

    /// <summary>The firm, and today as its clocks read it.</summary>
    private async Task<Result<(FirmId Firm, DateOnly Today)>> ScopeAsync(CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<(FirmId, DateOnly)>(NoFirm());
        }

        Firm? firm = await _firms.FindAsync(firmId, cancellationToken);
        TimeZoneInfo zone = TimeZoneInfo.Utc;

        if (firm is not null && !string.IsNullOrWhiteSpace(firm.TimeZoneId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(firm.TimeZoneId, out TimeZoneInfo? found))
        {
            zone = found;
        }

        return Result.Success((firmId, _clock.TodayIn(zone)));
    }
}

/// <summary>Validates <see cref="OpenPosShiftCommand"/>.</summary>
public sealed class OpenPosShiftCommandValidator : AbstractValidator<OpenPosShiftCommand>
{
    /// <summary>Initialises a new instance of the <see cref="OpenPosShiftCommandValidator"/> class.</summary>
    public OpenPosShiftCommandValidator() => RuleFor(c => c.OpeningCash).GreaterThanOrEqualTo(0m);
}

/// <summary>Validates <see cref="ClosePosShiftCommand"/>.</summary>
public sealed class ClosePosShiftCommandValidator : AbstractValidator<ClosePosShiftCommand>
{
    /// <summary>Initialises a new instance of the <see cref="ClosePosShiftCommandValidator"/> class.</summary>
    public ClosePosShiftCommandValidator()
    {
        RuleFor(c => c.ShiftId).NotEqual(Guid.Empty);
        RuleFor(c => c.CountedCash).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.Notes).MaximumLength(PosShift.MaximumNotesLength);
    }
}

/// <summary>Validates <see cref="ClosePosDayCommand"/>.</summary>
public sealed class ClosePosDayCommandValidator : AbstractValidator<ClosePosDayCommand>
{
    /// <summary>Initialises a new instance of the <see cref="ClosePosDayCommandValidator"/> class.</summary>
    public ClosePosDayCommandValidator() => RuleFor(c => c.Notes).MaximumLength(PosDayClose.MaximumNotesLength);
}
