using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Produces the day book: every voucher posted in a date range, in the order it
/// was entered.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="VoucherType">
/// Restricts the register to one kind of voucher. Left null, every kind appears.
/// </param>
/// <remarks>
/// The day book answers "what happened on this date", which no balance-oriented
/// report can. A trial balance shows where a firm stands; the day book shows what
/// it did, and it is the report an auditor asks for first when reconciling a
/// period.
/// </remarks>
public sealed record GetDayBookQuery(
    DateOnly From,
    DateOnly To,
    VoucherType? VoucherType = null) : IQuery<DayBookResponse>;

/// <summary>One posting line beneath a day-book entry.</summary>
/// <param name="LedgerId">The ledger posted to.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="Narration">The line narration, if it carries one.</param>
/// <param name="Debit">The amount when the line debits the ledger.</param>
/// <param name="Credit">The amount when the line credits the ledger.</param>
public sealed record DayBookLine(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string? Narration,
    decimal Debit,
    decimal Credit);

/// <summary>One voucher in the day book, with the lines that make it up.</summary>
/// <param name="VoucherId">The voucher, so the client can drill through to it.</param>
/// <param name="Date">The document date.</param>
/// <param name="VoucherNumber">The document number.</param>
/// <param name="VoucherType">The kind of voucher.</param>
/// <param name="ReferenceNumber">The related reference, if any.</param>
/// <param name="Narration">The voucher narration.</param>
/// <param name="Amount">
/// The value of the voucher: its total debits, which equal its total credits.
/// </param>
/// <param name="Lines">The postings, debits before credits.</param>
public sealed record DayBookEntry(
    Guid VoucherId,
    DateOnly Date,
    string VoucherNumber,
    VoucherType VoucherType,
    string? ReferenceNumber,
    string? Narration,
    decimal Amount,
    IReadOnlyList<DayBookLine> Lines);

/// <summary>The day book.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="TotalDebit">Total debits across the period.</param>
/// <param name="TotalCredit">Total credits across the period.</param>
/// <param name="VoucherCount">How many vouchers the period contains.</param>
/// <param name="Entries">The vouchers, oldest first.</param>
/// <remarks>
/// <see cref="TotalDebit"/> and <see cref="TotalCredit"/> must be equal. They are
/// both reported anyway, precisely so a reader can see at a glance that they are:
/// a day book whose columns disagree is the clearest possible signal that
/// something has posted incorrectly.
/// </remarks>
public sealed record DayBookResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal TotalDebit,
    decimal TotalCredit,
    int VoucherCount,
    IReadOnlyList<DayBookEntry> Entries);

/// <summary>Validates a <see cref="GetDayBookQuery"/>.</summary>
public sealed class GetDayBookQueryValidator : AbstractValidator<GetDayBookQuery>
{
    /// <summary>The longest period the day book will return in one request.</summary>
    /// <remarks>
    /// A busy branch posts hundreds of vouchers a day, each with several lines, and
    /// the day book returns every line. An unbounded range would let a single
    /// request pull a year of postings into memory and hold a connection while it
    /// serialises. A year is well beyond any legitimate day-book request; anything
    /// larger belongs in an exported report.
    /// </remarks>
    public const int MaximumRangeDays = 366;

    /// <summary>Initialises a new instance of the <see cref="GetDayBookQueryValidator"/> class.</summary>
    public GetDayBookQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A day book cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        RuleFor(q => q.VoucherType)
            .IsInEnum()
            .When(q => q.VoucherType.HasValue)
            .WithMessage("That is not a recognised voucher type.");
    }
}

/// <summary>Reads the vouchers behind the day book.</summary>
public interface IDayBookReader
{
    /// <summary>Reads every posted voucher in a period, with its lines.</summary>
    /// <param name="firmId">The firm, so another firm's vouchers cannot be read.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="voucherType">The kind to restrict to, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The vouchers, oldest first.</returns>
    Task<IReadOnlyList<DayBookEntry>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherType? voucherType,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetDayBookQuery"/>.</summary>
public sealed class GetDayBookQueryHandler : IQueryHandler<GetDayBookQuery, DayBookResponse>
{
    private readonly IDayBookReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetDayBookQueryHandler"/> class.</summary>
    /// <param name="reader">The day-book reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetDayBookQueryHandler(
        IDayBookReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<DayBookResponse>> Handle(
        GetDayBookQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<DayBookResponse>(firm.Error);
        }

        IReadOnlyList<DayBookEntry> entries = await _reader.ReadAsync(
            firm.Value.Id, request.From, request.To, request.VoucherType, cancellationToken);

        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach (DayBookEntry entry in entries)
        {
            foreach (DayBookLine line in entry.Lines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }
        }

        return Result.Success(new DayBookResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            totalDebit,
            totalCredit,
            entries.Count,
            entries));
    }
}
