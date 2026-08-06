using ERP.Application.Accounting.Reports;
using ERP.Application.Platform.Dashboards;
using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Platform;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the dashboards a user has been assigned.</summary>
public sealed class DashboardReader : IDashboardReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="DashboardReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public DashboardReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DashboardView>> ReadForUserAsync(
        FirmId firmId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        // Every dashboard assigned to any role the user holds. Distinct, because
        // overlapping audiences are the ordinary case - somebody who is both an
        // accountant and an administrator would otherwise see the same dashboard twice.
        List<DashboardId> assigned = await _context.Set<DashboardRole>()
            .Join(
                _context.Set<UserRole>().Where(userRole => userRole.UserId == userId),
                assignment => assignment.RoleId,
                userRole => userRole.RoleId,
                (assignment, _) => assignment.DashboardId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (assigned.Count == 0)
        {
            return [];
        }

        var dashboards = await _context.Dashboards
            .Where(dashboard =>
                dashboard.FirmId == firmId && assigned.Contains(dashboard.Id))
            .OrderBy(dashboard => dashboard.SortOrder)
            .ThenBy(dashboard => dashboard.Name)
            .Select(dashboard => new
            {
                dashboard.Id,
                dashboard.Code,
                dashboard.Name,
                dashboard.NameArabic,
                Widgets = dashboard.Widgets
                    .OrderBy(widget => widget.SortOrder)
                    .Select(widget => new DashboardWidgetView(
                        widget.Id.Value,
                        widget.MetricCode,
                        widget.Title,
                        widget.TitleArabic,
                        widget.Kind,
                        widget.Span,
                        widget.Query != null))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. dashboards.Select(dashboard => new DashboardView(
                dashboard.Id.Value,
                dashboard.Code,
                dashboard.Name,
                dashboard.NameArabic,
                dashboard.Widgets)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> ReadWidgetQueriesAsync(
        Guid dashboardId,
        CancellationToken cancellationToken = default)
    {
        DashboardId id = DashboardId.From(dashboardId);

        var queries = await _context.Set<DashboardWidget>()
            .Where(widget => widget.DashboardId == id && widget.Query != null)
            .Select(widget => new { widget.Id, widget.Query })
            .ToListAsync(cancellationToken);

        return queries.ToDictionary(row => row.Id.Value, row => row.Query!);
    }
}

/// <summary>Computes dashboard metrics.</summary>
/// <remarks>
/// Built on the report readers rather than on fresh SQL of its own. Every figure here
/// already appears on a report somebody can open, and a dashboard whose headline
/// receivables figure disagreed with the debtors report behind it would be worse than
/// no dashboard - the point of a headline is that it is the same number, seen sooner.
/// </remarks>
public sealed class DashboardMetricReader : IDashboardMetricReader
{
    /// <summary>How many entries a ranked list returns.</summary>
    private const int RankedListSize = 5;

    /// <summary>How many months of history a trend covers.</summary>
    private const int TrendMonths = 12;

    private readonly ErpDbContext _context;
    private readonly IOutstandingBillsReader _bills;
    private readonly IChequeReportReader _cheques;
    private readonly ITransactionSummaryReader _transactions;

    /// <summary>Initialises a new instance of the <see cref="DashboardMetricReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="bills">The outstanding bills reader.</param>
    /// <param name="cheques">The cheque report reader.</param>
    /// <param name="transactions">The transaction summary reader.</param>
    public DashboardMetricReader(
        ErpDbContext context,
        IOutstandingBillsReader bills,
        IChequeReportReader cheques,
        ITransactionSummaryReader transactions)
    {
        _context = context;
        _bills = bills;
        _cheques = cheques;
        _transactions = transactions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DashboardMetric>> ReadAsync(
        FirmId firmId,
        IReadOnlyCollection<string> metricCodes,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metricCodes);

        Dictionary<string, DashboardMetric> results = new(StringComparer.Ordinal);

        // The two bill-derived metrics and the ranked list all read the same rows, so
        // the reader is asked once and the answers are cut from the one result.
        bool needsReceivables =
            metricCodes.Contains(DashboardMetrics.Receivables)
            || metricCodes.Contains(DashboardMetrics.TopDebtors);

        if (needsReceivables)
        {
            IReadOnlyList<OutstandingBillRow> rows = await _bills.ReadAsync(
                firmId, BillType.Receivable, asAt, null, cancellationToken);

            if (metricCodes.Contains(DashboardMetrics.Receivables))
            {
                results[DashboardMetrics.Receivables] = new DashboardMetric(
                    Guid.Empty,
                    DashboardMetrics.Receivables,
                    rows.Sum(row => row.OutstandingAmount),
                    rows.Count,
                    [],
                    IsPermitted: true);
            }

            if (metricCodes.Contains(DashboardMetrics.TopDebtors))
            {
                List<MetricPoint> ranked =
                [
                    .. rows
                        .GroupBy(row => row.LedgerName)
                        .Select(group => new MetricPoint(
                            group.Key, group.Sum(row => row.OutstandingAmount)))
                        .OrderByDescending(point => point.Value)
                        .Take(RankedListSize),
                ];

                results[DashboardMetrics.TopDebtors] = new DashboardMetric(
                    Guid.Empty,
                    DashboardMetrics.TopDebtors,
                    ranked.Sum(point => point.Value),
                    ranked.Count,
                    ranked,
                    IsPermitted: true);
            }
        }

        if (metricCodes.Contains(DashboardMetrics.Payables))
        {
            IReadOnlyList<OutstandingBillRow> rows = await _bills.ReadAsync(
                firmId, BillType.Payable, asAt, null, cancellationToken);

            results[DashboardMetrics.Payables] = new DashboardMetric(
                Guid.Empty,
                DashboardMetrics.Payables,
                rows.Sum(row => row.OutstandingAmount),
                rows.Count,
                [],
                IsPermitted: true);
        }

        if (metricCodes.Contains(DashboardMetrics.CashAndBank))
        {
            results[DashboardMetrics.CashAndBank] =
                await ReadCashPositionAsync(firmId, asAt, cancellationToken);
        }

        bool needsReceivedCheques = metricCodes.Contains(DashboardMetrics.PostDatedReceivable);
        bool needsIssuedCheques = metricCodes.Contains(DashboardMetrics.PostDatedPayable);

        if (needsReceivedCheques || needsIssuedCheques)
        {
            // Everything still in hand and dated after the reporting date, which is
            // exactly what the PDC report itself asks for.
            IReadOnlyList<ChequeReportRow> rows = await _cheques.ReadAsync(
                new ChequeReportCriteria(
                    firmId,
                    asAt.AddDays(1),
                    DateOnly.MaxValue,
                    ByInstrumentDate: true,
                    Direction: null,
                    ChequeStatus.Pending,
                    OpenOnly: true),
                cancellationToken);

            if (needsReceivedCheques)
            {
                results[DashboardMetrics.PostDatedReceivable] = Summarise(
                    DashboardMetrics.PostDatedReceivable,
                    rows.Where(row => row.Direction == ChequeDirection.Received));
            }

            if (needsIssuedCheques)
            {
                results[DashboardMetrics.PostDatedPayable] = Summarise(
                    DashboardMetrics.PostDatedPayable,
                    rows.Where(row => row.Direction == ChequeDirection.Issued));
            }
        }

        if (metricCodes.Contains(DashboardMetrics.MonthlyPostings))
        {
            results[DashboardMetrics.MonthlyPostings] =
                await ReadMonthlyPostingsAsync(firmId, asAt, cancellationToken);
        }

        return results;
    }

    /// <summary>Totals a set of cheques into one metric.</summary>
    private static DashboardMetric Summarise(
        string metricCode,
        IEnumerable<ChequeReportRow> rows)
    {
        List<ChequeReportRow> materialised = [.. rows];

        return new DashboardMetric(
            Guid.Empty,
            metricCode,
            materialised.Sum(row => row.Amount),
            materialised.Count,
            [],
            IsPermitted: true);
    }

    /// <summary>Totals every cash and bank account as at a date.</summary>
    /// <remarks>
    /// Includes each account's stored opening balance, without which the figure would
    /// be short by whatever the firm had in the bank when it started keeping books
    /// here - which for any real migration is most of it.
    /// </remarks>
    private async Task<DashboardMetric> ReadCashPositionAsync(
        FirmId firmId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var accounts = await _context.Ledgers
            .Where(ledger =>
                ledger.FirmId == firmId
                && (ledger.Kind == LedgerKind.Cash || ledger.Kind == LedgerKind.Bank))
            .Select(ledger => new
            {
                ledger.Id,
                ledger.OpeningBalance,
                ledger.OpeningBalanceSide,
            })
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return new DashboardMetric(
                Guid.Empty,
                DashboardMetrics.CashAndBank, 0m, 0, [], IsPermitted: true);
        }

        List<LedgerId> ids = [.. accounts.Select(account => account.Id)];

        decimal opening = accounts.Sum(account =>
            account.OpeningBalanceSide == EntrySide.Debit
                ? account.OpeningBalance
                : -account.OpeningBalance);

        decimal movement = await _context.VoucherLines
            .Where(line => ids.Contains(line.LedgerId))
            .Join(
                _context.Vouchers.Where(voucher =>
                    voucher.FirmId == firmId
                    && voucher.Status == VoucherStatus.Posted
                    && voucher.Date <= asAt),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, _) => line)
            .SumAsync(
                line => line.Side == EntrySide.Debit
                    ? line.BaseAmount.Amount
                    : -line.BaseAmount.Amount,
                cancellationToken);

        return new DashboardMetric(
            Guid.Empty,
            DashboardMetrics.CashAndBank,
            opening + movement,
            accounts.Count,
            [],
            IsPermitted: true);
    }

    /// <summary>Counts vouchers posted per month over the trailing year.</summary>
    private async Task<DashboardMetric> ReadMonthlyPostingsAsync(
        FirmId firmId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        DateOnly from = new DateOnly(asAt.Year, asAt.Month, 1).AddMonths(-(TrendMonths - 1));

        IReadOnlyList<TransactionSummaryBucket> cells = await _transactions.ReadAsync(
            firmId, from, asAt, VoucherStatus.Posted, cancellationToken);

        List<MetricPoint> series = [];

        // Every month in the window, including the empty ones. A trend that silently
        // omitted a quiet month would draw the line straight through it and report a
        // gap in trading as steady activity.
        for (int offset = 0; offset < TrendMonths; offset++)
        {
            DateOnly month = from.AddMonths(offset);

            decimal total = cells
                .Where(cell => cell.Year == month.Year && cell.Month == month.Month)
                .Sum(cell => cell.TotalAmount);

            series.Add(new MetricPoint($"{month.Year:D4}-{month.Month:D2}", total));
        }

        return new DashboardMetric(
            Guid.Empty,
            DashboardMetrics.MonthlyPostings,
            series.Sum(point => point.Value),
            cells.Sum(cell => cell.VoucherCount),
            series,
            IsPermitted: true);
    }
}
