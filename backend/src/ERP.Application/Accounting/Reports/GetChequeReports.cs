using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Lists post-dated cheques still in hand: the specification's PDC report.
/// </summary>
/// <param name="AsAt">
/// The date the position is stated as at. A cheque counts as post-dated only while
/// its own date is still in the future relative to this one.
/// </param>
/// <param name="Direction">
/// Received for cheques the firm is holding, issued for cheques it has written and
/// not yet seen presented. Omit for both.
/// </param>
/// <param name="LedgerId">Restricts the report to one party. Omit for all of them.</param>
/// <remarks>
/// What a treasurer reads to know what is coming, and what a credit controller reads
/// to know what has not arrived. Only pending cheques appear: one already with the
/// bank is no longer a promise, it is an outcome waiting to be reported.
/// </remarks>
public sealed record GetPostDatedChequesQuery(
    DateOnly AsAt,
    ChequeDirection? Direction = null,
    Guid? LedgerId = null) : IQuery<PostDatedChequesResponse>;

/// <summary>
/// Groups cheques by the day they fall due: the specification's PDC calendar.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="Direction">Received, issued, or both.</param>
/// <remarks>
/// The same rows as the PDC report, arranged by date rather than by party, because
/// the question is different: not "who owes what" but "what lands this week, and can
/// the account carry it".
/// </remarks>
public sealed record GetChequeCalendarQuery(
    DateOnly From,
    DateOnly To,
    ChequeDirection? Direction = null) : IQuery<ChequeCalendarResponse>;

/// <summary>
/// Lists every cheque taken in or written out over a period: the cheque register.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="Direction">Received, issued, or both.</param>
/// <param name="Status">Restricts to one lifecycle status. Omit for all.</param>
/// <param name="LedgerId">Restricts to one party. Omit for all.</param>
/// <remarks>
/// Read by when a cheque changed hands, not by when it falls due, and it shows
/// closed cheques as well as live ones - a register that dropped a cheque the moment
/// it cleared would be unable to answer the question it is usually opened for, which
/// is what happened to one.
/// </remarks>
public sealed record GetChequeRegisterQuery(
    DateOnly From,
    DateOnly To,
    ChequeDirection? Direction = null,
    ChequeStatus? Status = null,
    Guid? LedgerId = null) : IQuery<ChequeRegisterResponse>;

/// <summary>One cheque as it appears on a report.</summary>
/// <param name="ChequeId">The cheque, so the client can drill through to it.</param>
/// <param name="ChequeNumber">The number printed on it.</param>
/// <param name="Direction">Received or issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PartyLedgerId">The party.</param>
/// <param name="PartyCode">The party's ledger code.</param>
/// <param name="PartyName">The party's name.</param>
/// <param name="InstrumentDate">The date written on its face.</param>
/// <param name="RecordedOn">The date it changed hands.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Currency">The currency it is drawn in.</param>
/// <param name="BankName">
/// The firm's account for an issued cheque or a banked one; the bank named on the
/// face of a received cheque still in hand.
/// </param>
/// <param name="DepositedOn">When it entered the banking system, if it has.</param>
/// <param name="ClosedOn">When it reached its terminal state, if it has.</param>
/// <param name="ClosureReason">Why it bounced, was stopped, or was voided.</param>
/// <param name="DaysUntilDue">
/// Days until it may be banked, as at the reporting date. Zero once its date has
/// arrived, so a report can sort by how soon money is expected.
/// </param>
public sealed record ChequeReportLine(
    Guid ChequeId,
    string ChequeNumber,
    ChequeDirection Direction,
    ChequeStatus Status,
    Guid PartyLedgerId,
    string PartyCode,
    string PartyName,
    DateOnly InstrumentDate,
    DateOnly RecordedOn,
    decimal Amount,
    string Currency,
    string? BankName,
    DateOnly? DepositedOn,
    DateOnly? ClosedOn,
    string? ClosureReason,
    int DaysUntilDue);

/// <summary>The post-dated cheques in hand.</summary>
/// <param name="AsAt">The date the position is stated as at.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Cheques">The cheques, soonest due first.</param>
/// <param name="TotalReceivable">Total of post-dated cheques the firm is holding.</param>
/// <param name="TotalPayable">Total of post-dated cheques the firm has written.</param>
/// <param name="Currencies">
/// Every currency appearing in the report. A total means something only when this
/// holds a single entry.
/// </param>
public sealed record PostDatedChequesResponse(
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<ChequeReportLine> Cheques,
    decimal TotalReceivable,
    decimal TotalPayable,
    IReadOnlyList<string> Currencies);

/// <summary>One day of the cheque calendar.</summary>
/// <param name="Date">The day cheques fall due.</param>
/// <param name="Receivable">Total falling in that day.</param>
/// <param name="Payable">Total falling out that day.</param>
/// <param name="Net">
/// Receivable less payable: what the day does to the firm's position, which is the
/// figure a treasurer is actually looking for.
/// </param>
/// <param name="Cheques">The cheques due that day.</param>
public sealed record ChequeCalendarDay(
    DateOnly Date,
    decimal Receivable,
    decimal Payable,
    decimal Net,
    IReadOnlyList<ChequeReportLine> Cheques);

/// <summary>The cheque calendar.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Days">The days on which something falls due, in date order.</param>
/// <param name="TotalReceivable">Total due in over the period.</param>
/// <param name="TotalPayable">Total due out over the period.</param>
/// <param name="Currencies">Every currency appearing in the calendar.</param>
/// <remarks>
/// Only days with cheques on them appear. Filling in the empty ones would triple the
/// size of a quarter's calendar to say nothing, and a caller rendering a grid knows
/// its own date range better than this does.
/// </remarks>
public sealed record ChequeCalendarResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<ChequeCalendarDay> Days,
    decimal TotalReceivable,
    decimal TotalPayable,
    IReadOnlyList<string> Currencies);

/// <summary>The cheque register.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Cheques">The cheques, most recently taken in first.</param>
/// <param name="TotalReceived">Total of cheques taken in over the period.</param>
/// <param name="TotalIssued">Total of cheques written out over the period.</param>
/// <param name="CountByStatus">How many cheques stand in each status.</param>
/// <param name="Currencies">Every currency appearing in the register.</param>
public sealed record ChequeRegisterResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<ChequeReportLine> Cheques,
    decimal TotalReceived,
    decimal TotalIssued,
    IReadOnlyDictionary<ChequeStatus, int> CountByStatus,
    IReadOnlyList<string> Currencies);

/// <summary>Validates a <see cref="GetPostDatedChequesQuery"/>.</summary>
public sealed class GetPostDatedChequesQueryValidator
    : AbstractValidator<GetPostDatedChequesQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetPostDatedChequesQueryValidator"/> class.</summary>
    public GetPostDatedChequesQueryValidator()
    {
        RuleFor(q => q.AsAt).NotEqual(default(DateOnly));
        RuleFor(q => q.Direction!.Value).IsInEnum().When(q => q.Direction.HasValue);
    }
}

/// <summary>Validates a <see cref="GetChequeCalendarQuery"/>.</summary>
public sealed class GetChequeCalendarQueryValidator : AbstractValidator<GetChequeCalendarQuery>
{
    /// <summary>The longest period the calendar will return in one request.</summary>
    public const int MaximumRangeDays = 732;

    /// <summary>Initialises a new instance of the <see cref="GetChequeCalendarQueryValidator"/> class.</summary>
    public GetChequeCalendarQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        // Two years. A post-dated arrangement can genuinely run that long, which is
        // why this is looser than the cash book's year.
        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A cheque calendar cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        RuleFor(q => q.Direction!.Value).IsInEnum().When(q => q.Direction.HasValue);
    }
}

/// <summary>Validates a <see cref="GetChequeRegisterQuery"/>.</summary>
public sealed class GetChequeRegisterQueryValidator : AbstractValidator<GetChequeRegisterQuery>
{
    /// <summary>The longest period the register will return in one request.</summary>
    public const int MaximumRangeDays = 732;

    /// <summary>Initialises a new instance of the <see cref="GetChequeRegisterQueryValidator"/> class.</summary>
    public GetChequeRegisterQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A cheque register cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        RuleFor(q => q.Direction!.Value).IsInEnum().When(q => q.Direction.HasValue);
        RuleFor(q => q.Status!.Value).IsInEnum().When(q => q.Status.HasValue);
    }
}

/// <summary>What a cheque report is being asked for.</summary>
/// <param name="FirmId">The firm.</param>
/// <param name="From">
/// The first date included, against whichever date the report reads by.
/// </param>
/// <param name="To">The last date included.</param>
/// <param name="ByInstrumentDate">
/// Whether the range applies to the date on the cheque's face, as the PDC report and
/// calendar read it, or to the date it changed hands, as the register does.
/// </param>
/// <param name="Direction">Received, issued, or both.</param>
/// <param name="Status">One status, or all of them.</param>
/// <param name="OpenOnly">
/// Whether to exclude cheques that have already cleared, bounced, been stopped, or
/// been voided.
/// </param>
/// <param name="LedgerId">One party, or all of them.</param>
public sealed record ChequeReportCriteria(
    FirmId FirmId,
    DateOnly From,
    DateOnly To,
    bool ByInstrumentDate,
    ChequeDirection? Direction = null,
    ChequeStatus? Status = null,
    bool OpenOnly = false,
    LedgerId? LedgerId = null);

/// <summary>Reads the cheques behind the PDC report, calendar, and register.</summary>
/// <remarks>
/// One reader for all three. They differ in which date they read by and how they
/// arrange the result, not in what a cheque is - and three readers would eventually
/// disagree about that, while being read side by side.
/// </remarks>
public interface IChequeReportReader
{
    /// <summary>Reads the cheques matching a set of criteria.</summary>
    /// <param name="criteria">What is being asked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cheques with their party and bank, in no particular order.</returns>
    Task<IReadOnlyList<ChequeReportRow>> ReadAsync(
        ChequeReportCriteria criteria,
        CancellationToken cancellationToken = default);
}

/// <summary>One cheque and its party, before presentation.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="ChequeNumber">The number printed on it.</param>
/// <param name="Direction">Received or issued.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PartyLedgerId">The party.</param>
/// <param name="PartyCode">The party's ledger code.</param>
/// <param name="PartyName">The party's name.</param>
/// <param name="InstrumentDate">The date written on its face.</param>
/// <param name="RecordedOn">The date it changed hands.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Currency">The currency it is drawn in.</param>
/// <param name="BankAccountName">The firm's account, once one is known.</param>
/// <param name="DrawnOnBank">The bank named on a received cheque.</param>
/// <param name="DepositedOn">When it entered the banking system.</param>
/// <param name="ClosedOn">When it reached its terminal state.</param>
/// <param name="ClosureReason">Why it bounced, was stopped, or was voided.</param>
public sealed record ChequeReportRow(
    Guid ChequeId,
    string ChequeNumber,
    ChequeDirection Direction,
    ChequeStatus Status,
    Guid PartyLedgerId,
    string PartyCode,
    string PartyName,
    DateOnly InstrumentDate,
    DateOnly RecordedOn,
    decimal Amount,
    string Currency,
    string? BankAccountName,
    string? DrawnOnBank,
    DateOnly? DepositedOn,
    DateOnly? ClosedOn,
    string? ClosureReason);

/// <summary>Shapes reader rows into report lines.</summary>
internal static class ChequeReportLines
{
    /// <summary>Presents one row as at a reporting date.</summary>
    /// <param name="row">The row.</param>
    /// <param name="asAt">The date the report is stated as at.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// The firm's own account is preferred over the bank named on the cheque's face,
    /// because once a cheque is banked that is the account a reader is reconciling
    /// against. A received cheque still in hand has no firm account yet, and the
    /// payer's bank is the only thing there is to show.
    /// </remarks>
    internal static ChequeReportLine From(ChequeReportRow row, DateOnly asAt) => new(
        row.ChequeId,
        row.ChequeNumber,
        row.Direction,
        row.Status,
        row.PartyLedgerId,
        row.PartyCode,
        row.PartyName,
        row.InstrumentDate,
        row.RecordedOn,
        row.Amount,
        row.Currency,
        row.BankAccountName ?? row.DrawnOnBank,
        row.DepositedOn,
        row.ClosedOn,
        row.ClosureReason,
        DaysUntilDue(row.InstrumentDate, asAt));

    /// <summary>Counts days until a cheque may be banked, never negative.</summary>
    /// <param name="instrumentDate">The date on the cheque's face.</param>
    /// <param name="asAt">The reporting date.</param>
    /// <returns>The days remaining, or zero once its date has arrived.</returns>
    internal static int DaysUntilDue(DateOnly instrumentDate, DateOnly asAt)
    {
        int days = instrumentDate.DayNumber - asAt.DayNumber;

        return days > 0 ? days : 0;
    }
}

/// <summary>Handles <see cref="GetPostDatedChequesQuery"/>.</summary>
public sealed class GetPostDatedChequesQueryHandler
    : IQueryHandler<GetPostDatedChequesQuery, PostDatedChequesResponse>
{
    private readonly IChequeReportReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetPostDatedChequesQueryHandler"/> class.</summary>
    /// <param name="reader">The cheque report reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetPostDatedChequesQueryHandler(
        IChequeReportReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<PostDatedChequesResponse>> Handle(
        GetPostDatedChequesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<PostDatedChequesResponse>(firm.Error);
        }

        // Everything dated after the reporting date, and only what is still in hand.
        // A cheque already with the bank is no longer a promise - it is an outcome
        // waiting to be reported, and belongs on the register rather than here.
        IReadOnlyList<ChequeReportRow> rows = await _reader.ReadAsync(
            new ChequeReportCriteria(
                firm.Value.Id,
                request.AsAt.AddDays(1),
                DateOnly.MaxValue,
                ByInstrumentDate: true,
                request.Direction,
                ChequeStatus.Pending,
                OpenOnly: true,
                request.LedgerId is { } id ? LedgerId.From(id) : null),
            cancellationToken);

        List<ChequeReportLine> lines = [];

        decimal receivable = 0m;
        decimal payable = 0m;
        HashSet<string> currencies = new(StringComparer.Ordinal);

        // Soonest first, then by party, so the cheque that matures next is the one a
        // treasurer reads first.
        foreach (ChequeReportRow row in rows
            .OrderBy(r => r.InstrumentDate)
            .ThenBy(r => r.PartyCode, StringComparer.Ordinal)
            .ThenBy(r => r.ChequeNumber, StringComparer.Ordinal))
        {
            lines.Add(ChequeReportLines.From(row, request.AsAt));
            currencies.Add(row.Currency);

            if (row.Direction == ChequeDirection.Received)
            {
                receivable += row.Amount;
            }
            else
            {
                payable += row.Amount;
            }
        }

        return Result.Success(new PostDatedChequesResponse(
            request.AsAt,
            firm.Value.BaseCurrency.Code,
            lines,
            receivable,
            payable,
            [.. currencies.Order(StringComparer.Ordinal)]));
    }
}

/// <summary>Handles <see cref="GetChequeCalendarQuery"/>.</summary>
public sealed class GetChequeCalendarQueryHandler
    : IQueryHandler<GetChequeCalendarQuery, ChequeCalendarResponse>
{
    private readonly IChequeReportReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetChequeCalendarQueryHandler"/> class.</summary>
    /// <param name="reader">The cheque report reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetChequeCalendarQueryHandler(
        IChequeReportReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeCalendarResponse>> Handle(
        GetChequeCalendarQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<ChequeCalendarResponse>(firm.Error);
        }

        // Open cheques only. A calendar showing what a cleared cheque would have done
        // is a record of the past pretending to be a forecast.
        IReadOnlyList<ChequeReportRow> rows = await _reader.ReadAsync(
            new ChequeReportCriteria(
                firm.Value.Id,
                request.From,
                request.To,
                ByInstrumentDate: true,
                request.Direction,
                Status: null,
                OpenOnly: true),
            cancellationToken);

        List<ChequeCalendarDay> days = [];

        decimal totalReceivable = 0m;
        decimal totalPayable = 0m;
        HashSet<string> currencies = new(StringComparer.Ordinal);

        foreach (IGrouping<DateOnly, ChequeReportRow> group in rows
            .GroupBy(r => r.InstrumentDate)
            .OrderBy(group => group.Key))
        {
            List<ChequeReportLine> lines = [];

            decimal receivable = 0m;
            decimal payable = 0m;

            foreach (ChequeReportRow row in group
                .OrderBy(r => r.PartyCode, StringComparer.Ordinal)
                .ThenBy(r => r.ChequeNumber, StringComparer.Ordinal))
            {
                lines.Add(ChequeReportLines.From(row, group.Key));
                currencies.Add(row.Currency);

                if (row.Direction == ChequeDirection.Received)
                {
                    receivable += row.Amount;
                }
                else
                {
                    payable += row.Amount;
                }
            }

            days.Add(new ChequeCalendarDay(
                group.Key, receivable, payable, receivable - payable, lines));

            totalReceivable += receivable;
            totalPayable += payable;
        }

        return Result.Success(new ChequeCalendarResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            days,
            totalReceivable,
            totalPayable,
            [.. currencies.Order(StringComparer.Ordinal)]));
    }
}

/// <summary>Handles <see cref="GetChequeRegisterQuery"/>.</summary>
public sealed class GetChequeRegisterQueryHandler
    : IQueryHandler<GetChequeRegisterQuery, ChequeRegisterResponse>
{
    private readonly IChequeReportReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetChequeRegisterQueryHandler"/> class.</summary>
    /// <param name="reader">The cheque report reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetChequeRegisterQueryHandler(
        IChequeReportReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeRegisterResponse>> Handle(
        GetChequeRegisterQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<ChequeRegisterResponse>(firm.Error);
        }

        IReadOnlyList<ChequeReportRow> rows = await _reader.ReadAsync(
            new ChequeReportCriteria(
                firm.Value.Id,
                request.From,
                request.To,
                ByInstrumentDate: false,
                request.Direction,
                request.Status,
                OpenOnly: false,
                request.LedgerId is { } id ? LedgerId.From(id) : null),
            cancellationToken);

        List<ChequeReportLine> lines = [];
        Dictionary<ChequeStatus, int> counts = [];

        decimal received = 0m;
        decimal issued = 0m;
        HashSet<string> currencies = new(StringComparer.Ordinal);

        // Most recently taken in first. A register is usually opened to find
        // something recent, and paging from the newest end is what a reader expects.
        foreach (ChequeReportRow row in rows
            .OrderByDescending(r => r.RecordedOn)
            .ThenBy(r => r.PartyCode, StringComparer.Ordinal)
            .ThenBy(r => r.ChequeNumber, StringComparer.Ordinal))
        {
            lines.Add(ChequeReportLines.From(row, request.To));
            currencies.Add(row.Currency);
            counts[row.Status] = counts.GetValueOrDefault(row.Status) + 1;

            if (row.Direction == ChequeDirection.Received)
            {
                received += row.Amount;
            }
            else
            {
                issued += row.Amount;
            }
        }

        return Result.Success(new ChequeRegisterResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            lines,
            received,
            issued,
            counts,
            [.. currencies.Order(StringComparer.Ordinal)]));
    }
}
