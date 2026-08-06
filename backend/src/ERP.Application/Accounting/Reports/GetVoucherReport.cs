using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Produces the voucher report: a register of vouchers by document, filterable by
/// type and by status.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="Type">Restricts the register to one kind of voucher. Omit for all.</param>
/// <param name="Status">
/// Restricts to one lifecycle status. Omit for all - the register's reason to exist
/// beside the day book is that it can show drafts and cancelled vouchers, which the
/// day book, being posted-only, never can.
/// </param>
/// <remarks>
/// Where the day book reads a period line by line, the voucher report reads it
/// document by document: one row per voucher, its value rather than its postings, and
/// across every status rather than the posted ones alone. It is what an operator opens
/// to find a particular voucher - the cancelled payment somebody is asking about, the
/// drafts still waiting to be posted - and what a reviewer opens to work through a
/// day's entries one at a time.
/// </remarks>
public sealed record GetVoucherReportQuery(
    DateOnly From,
    DateOnly To,
    VoucherType? Type = null,
    VoucherStatus? Status = null) : IQuery<VoucherReportResponse>;

/// <summary>One voucher on the register.</summary>
/// <param name="VoucherId">The voucher, so the client can drill through to it.</param>
/// <param name="Date">The document date.</param>
/// <param name="VoucherNumber">The document number.</param>
/// <param name="Type">The kind of voucher.</param>
/// <param name="Status">Where it stands: draft, posted, or cancelled.</param>
/// <param name="ReferenceNumber">The related reference, if any.</param>
/// <param name="Narration">The voucher narration.</param>
/// <param name="Currency">The currency the voucher was entered in.</param>
/// <param name="ExchangeRate">The rate that converted it to the base currency.</param>
/// <param name="DocumentAmount">
/// The voucher's value in its own currency: its total debits, which equal its total
/// credits.
/// </param>
/// <param name="BaseAmount">The same value converted to the firm's base currency.</param>
public sealed record VoucherReportLine(
    Guid VoucherId,
    DateOnly Date,
    string VoucherNumber,
    VoucherType Type,
    VoucherStatus Status,
    string? ReferenceNumber,
    string? Narration,
    string Currency,
    decimal ExchangeRate,
    decimal DocumentAmount,
    decimal BaseAmount);

/// <summary>The voucher report.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Vouchers">The vouchers, most recent first.</param>
/// <param name="VoucherCount">How many vouchers the register contains.</param>
/// <param name="TotalBaseAmount">
/// The total value of the listed vouchers in the base currency. Base rather than
/// document currency because it is the one figure that can be summed across vouchers
/// drawn in different currencies.
/// </param>
/// <param name="CountByStatus">How many vouchers stand in each status.</param>
/// <param name="Currencies">
/// Every document currency appearing in the register. Where it holds more than one
/// entry the vouchers were not all drawn in the base currency, and a reader comparing
/// document amounts should notice.
/// </param>
public sealed record VoucherReportResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<VoucherReportLine> Vouchers,
    int VoucherCount,
    decimal TotalBaseAmount,
    IReadOnlyDictionary<VoucherStatus, int> CountByStatus,
    IReadOnlyList<string> Currencies);

/// <summary>Validates a <see cref="GetVoucherReportQuery"/>.</summary>
public sealed class GetVoucherReportQueryValidator : AbstractValidator<GetVoucherReportQuery>
{
    /// <summary>The longest period the register will return in one request.</summary>
    /// <remarks>
    /// A year, matching the day book. The voucher report is lighter - one row per
    /// voucher rather than every line - but an unbounded range still lets a single
    /// request pull a firm's entire history, and a year is beyond any ordinary lookup.
    /// </remarks>
    public const int MaximumRangeDays = 366;

    /// <summary>Initialises a new instance of the <see cref="GetVoucherReportQueryValidator"/> class.</summary>
    public GetVoucherReportQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A voucher report cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        RuleFor(q => q.Type!.Value)
            .IsInEnum()
            .When(q => q.Type.HasValue)
            .WithMessage("That is not a recognised voucher type.");

        RuleFor(q => q.Status!.Value)
            .IsInEnum()
            .When(q => q.Status.HasValue)
            .WithMessage("That is not a recognised voucher status.");
    }
}

/// <summary>Reads the vouchers behind the voucher report.</summary>
/// <remarks>
/// Header-level: one row per voucher carrying its value, not its postings. The value
/// is summed from the lines in the database rather than by loading them, because the
/// register exists precisely so a reader does not have to open every voucher to see
/// what it was for.
/// </remarks>
public interface IVoucherReportReader
{
    /// <summary>Reads the vouchers matching a set of filters.</summary>
    /// <param name="firmId">The firm, so another firm's vouchers cannot be read.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="type">The kind to restrict to, or null for all.</param>
    /// <param name="status">The status to restrict to, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The vouchers with their values, in no particular order.</returns>
    Task<IReadOnlyList<VoucherReportLine>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherType? type,
        VoucherStatus? status,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetVoucherReportQuery"/>.</summary>
public sealed class GetVoucherReportQueryHandler
    : IQueryHandler<GetVoucherReportQuery, VoucherReportResponse>
{
    private readonly IVoucherReportReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetVoucherReportQueryHandler"/> class.</summary>
    /// <param name="reader">The voucher report reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetVoucherReportQueryHandler(
        IVoucherReportReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<VoucherReportResponse>> Handle(
        GetVoucherReportQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<VoucherReportResponse>(firm.Error);
        }

        IReadOnlyList<VoucherReportLine> rows = await _reader.ReadAsync(
            firm.Value.Id, request.From, request.To, request.Type, request.Status,
            cancellationToken);

        List<VoucherReportLine> vouchers = [];
        Dictionary<VoucherStatus, int> counts = [];

        decimal totalBaseAmount = 0m;
        HashSet<string> currencies = new(StringComparer.Ordinal);

        // Most recent first, then by document number: a register is usually opened to
        // find something recent, and paging from the newest end is what a reader
        // expects. Number breaks ties within a day so the order is stable across
        // requests rather than left to the database.
        foreach (VoucherReportLine row in rows
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.VoucherNumber, StringComparer.Ordinal))
        {
            vouchers.Add(row);
            counts[row.Status] = counts.GetValueOrDefault(row.Status) + 1;
            totalBaseAmount += row.BaseAmount;
            currencies.Add(row.Currency);
        }

        return Result.Success(new VoucherReportResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            vouchers,
            vouchers.Count,
            totalBaseAmount,
            counts,
            [.. currencies.Order(StringComparer.Ordinal)]));
    }
}
