using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Produces the transaction summary: how much was posted over a period, by voucher
/// type and by month.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="Status">
/// Restricts the summary to one status. Omit for all, which is the useful default -
/// the count of drafts sitting unposted is one of the things this report is opened to
/// find out.
/// </param>
/// <remarks>
/// The third tier of the same postings. The day book reads a period line by line, the
/// voucher report document by document, and this reads it in totals: no individual
/// voucher at all, just how many of each kind and what they came to. It is the control
/// total an auditor ticks against, and the shape of a month's activity a manager reads
/// before deciding which report to open next.
/// </remarks>
public sealed record GetTransactionSummaryQuery(
    DateOnly From,
    DateOnly To,
    VoucherStatus? Status = null) : IQuery<TransactionSummaryResponse>;

/// <summary>One voucher type's totals over the period.</summary>
/// <param name="Type">The kind of voucher.</param>
/// <param name="VoucherCount">How many were raised.</param>
/// <param name="TotalAmount">Their total value in the base currency.</param>
/// <param name="CountByStatus">How many of them stand in each status.</param>
public sealed record TransactionSummaryType(
    VoucherType Type,
    int VoucherCount,
    decimal TotalAmount,
    IReadOnlyDictionary<VoucherStatus, int> CountByStatus);

/// <summary>One month's totals across every voucher type.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month, 1 through 12.</param>
/// <param name="VoucherCount">How many vouchers the month carries.</param>
/// <param name="TotalAmount">Their total value in the base currency.</param>
/// <remarks>
/// Calendar months rather than the financial year's periods. A financial year that
/// starts in April still has Januaries in it, and a reader comparing this to a bank
/// statement or a VAT return is working in calendar months.
/// </remarks>
public sealed record TransactionSummaryMonth(
    int Year,
    int Month,
    int VoucherCount,
    decimal TotalAmount);

/// <summary>The transaction summary.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="Types">One row per voucher type present, in the type's own order.</param>
/// <param name="Months">
/// One row per month that carries something, oldest first. Empty months are left out
/// rather than filled in with zeros: a caller drawing a chart knows its own axis, and
/// a year of mostly-empty rows says nothing.
/// </param>
/// <param name="VoucherCount">How many vouchers the period contains in total.</param>
/// <param name="TotalAmount">Their total value in the base currency.</param>
/// <param name="CountByStatus">How many vouchers stand in each status.</param>
/// <remarks>
/// Figures are stated in the base currency throughout. A summary is a total by
/// definition, and a total across vouchers drawn in different currencies is only
/// meaningful once they are converted - which the posting has already done.
/// </remarks>
public sealed record TransactionSummaryResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<TransactionSummaryType> Types,
    IReadOnlyList<TransactionSummaryMonth> Months,
    int VoucherCount,
    decimal TotalAmount,
    IReadOnlyDictionary<VoucherStatus, int> CountByStatus);

/// <summary>Validates a <see cref="GetTransactionSummaryQuery"/>.</summary>
public sealed class GetTransactionSummaryQueryValidator
    : AbstractValidator<GetTransactionSummaryQuery>
{
    /// <summary>The longest period the summary will return in one request.</summary>
    /// <remarks>
    /// Three years, deliberately looser than the day book's one. The summary returns a
    /// handful of aggregated rows however long the range, so the cost is in the
    /// database's grouping rather than in what comes back, and comparing this year
    /// against the last two is exactly what the report is for.
    /// </remarks>
    public const int MaximumRangeDays = 1_098;

    /// <summary>Initialises a new instance of the <see cref="GetTransactionSummaryQueryValidator"/> class.</summary>
    public GetTransactionSummaryQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A transaction summary cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);

        RuleFor(q => q.Status!.Value)
            .IsInEnum()
            .When(q => q.Status.HasValue)
            .WithMessage("That is not a recognised voucher status.");
    }
}

/// <summary>
/// One cell of the summary: a type and status within a month, already counted and
/// totalled.
/// </summary>
/// <param name="Type">The kind of voucher.</param>
/// <param name="Status">Where those vouchers stand.</param>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The calendar month, 1 through 12.</param>
/// <param name="VoucherCount">How many vouchers fall in this cell.</param>
/// <param name="TotalAmount">Their total value in the base currency.</param>
public sealed record TransactionSummaryBucket(
    VoucherType Type,
    VoucherStatus Status,
    int Year,
    int Month,
    int VoucherCount,
    decimal TotalAmount);

/// <summary>Reads the aggregated cells behind the transaction summary.</summary>
/// <remarks>
/// The counting and totalling happen in the database. The report's whole point is that
/// it does not need the vouchers themselves, and loading a year of them to count them
/// in memory would make the cheapest report in the system the most expensive.
/// <para>
/// Cells are cut finely enough - by type, by status, and by month - that the handler
/// can pivot them into every view the report presents without going back to the
/// database. The result set is bounded by those three dimensions rather than by how
/// busy the firm is.
/// </para>
/// </remarks>
public interface ITransactionSummaryReader
{
    /// <summary>Aggregates the vouchers of a period into cells.</summary>
    /// <param name="firmId">The firm, so another firm's vouchers cannot be read.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="status">The status to restrict to, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cells, in no particular order.</returns>
    Task<IReadOnlyList<TransactionSummaryBucket>> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        VoucherStatus? status,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetTransactionSummaryQuery"/>.</summary>
public sealed class GetTransactionSummaryQueryHandler
    : IQueryHandler<GetTransactionSummaryQuery, TransactionSummaryResponse>
{
    private readonly ITransactionSummaryReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetTransactionSummaryQueryHandler"/> class.</summary>
    /// <param name="reader">The summary reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetTransactionSummaryQueryHandler(
        ITransactionSummaryReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<TransactionSummaryResponse>> Handle(
        GetTransactionSummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<TransactionSummaryResponse>(firm.Error);
        }

        IReadOnlyList<TransactionSummaryBucket> cells = await _reader.ReadAsync(
            firm.Value.Id, request.From, request.To, request.Status, cancellationToken);

        // Both views are pivoted from the one set of cells. Asking the database twice
        // for the same postings cut two ways would leave the by-type and by-month
        // totals free to disagree, which on a report whose only content is totals
        // would be the whole of it wrong.
        List<TransactionSummaryType> types = [];

        foreach (IGrouping<VoucherType, TransactionSummaryBucket> group in cells
            .GroupBy(cell => cell.Type)
            .OrderBy(group => group.Key))
        {
            Dictionary<VoucherStatus, int> countByStatus = [];

            foreach (TransactionSummaryBucket cell in group)
            {
                countByStatus[cell.Status] =
                    countByStatus.GetValueOrDefault(cell.Status) + cell.VoucherCount;
            }

            types.Add(new TransactionSummaryType(
                group.Key,
                group.Sum(cell => cell.VoucherCount),
                group.Sum(cell => cell.TotalAmount),
                countByStatus));
        }

        List<TransactionSummaryMonth> months =
        [
            .. cells
                .GroupBy(cell => (cell.Year, cell.Month))
                .OrderBy(group => group.Key.Year)
                .ThenBy(group => group.Key.Month)
                .Select(group => new TransactionSummaryMonth(
                    group.Key.Year,
                    group.Key.Month,
                    group.Sum(cell => cell.VoucherCount),
                    group.Sum(cell => cell.TotalAmount))),
        ];

        Dictionary<VoucherStatus, int> totalByStatus = [];

        foreach (TransactionSummaryBucket cell in cells)
        {
            totalByStatus[cell.Status] =
                totalByStatus.GetValueOrDefault(cell.Status) + cell.VoucherCount;
        }

        return Result.Success(new TransactionSummaryResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            types,
            months,
            cells.Sum(cell => cell.VoucherCount),
            cells.Sum(cell => cell.TotalAmount),
            totalByStatus));
    }
}
