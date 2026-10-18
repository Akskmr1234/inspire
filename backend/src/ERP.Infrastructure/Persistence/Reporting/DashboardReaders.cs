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

    /// <summary>The group the sales ledgers sit under.</summary>
    private const string SalesAccounts = "4100";

    /// <summary>The group the purchase and cost-of-goods ledgers sit under.</summary>
    private const string PurchaseAccounts = "5100";

    private readonly ErpDbContext _context;
    private readonly IOutstandingBillsReader _bills;
    private readonly IChequeReportReader _cheques;
    private readonly ITransactionSummaryReader _transactions;
    private readonly ITrialBalanceReader _balances;

    /// <summary>Initialises a new instance of the <see cref="DashboardMetricReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="bills">The outstanding bills reader.</param>
    /// <param name="cheques">The cheque report reader.</param>
    /// <param name="transactions">The transaction summary reader.</param>
    /// <param name="balances">The ledger movement reader behind the trading figures.</param>
    public DashboardMetricReader(
        ErpDbContext context,
        IOutstandingBillsReader bills,
        IChequeReportReader cheques,
        ITransactionSummaryReader transactions,
        ITrialBalanceReader balances)
    {
        _context = context;
        _bills = bills;
        _cheques = cheques;
        _transactions = transactions;
        _balances = balances;
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

        await ReadTradingAsync(firmId, metricCodes, asAt, results, cancellationToken);

        return results;
    }

    /// <summary>Sales, running expenses and profit, for the day and for the month.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="metricCodes">The metrics asked for.</param>
    /// <param name="asAt">The day the figures are for.</param>
    /// <param name="results">Where the figures go.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// <para>
    /// From the same ledger movements the profit and loss account reads, so a tile and
    /// the statement for the same days always agree. Stock is perpetual - a sale posts
    /// its cost of goods as it goes - so income less expense over a period is the
    /// period's profit without any closing-stock adjustment.
    /// </para>
    /// <para>
    /// Sales are the income ledgers under Sales Accounts, net of returns. Expenses are
    /// the running costs: every expense ledger except those under Purchase Accounts,
    /// where the cost of goods sold sits. A shopkeeper asking what was spent today means
    /// rent, wages and electricity, not the cost of what went out of the door.
    /// </para>
    /// </remarks>
    private async Task ReadTradingAsync(
        FirmId firmId,
        IReadOnlyCollection<string> metricCodes,
        DateOnly asAt,
        Dictionary<string, DashboardMetric> results,
        CancellationToken cancellationToken)
    {
        (string Code, bool Month, Func<Trading, decimal> Figure)[] wanted =
        [
            (DashboardMetrics.SalesToday, false, trading => trading.Sales),
            (DashboardMetrics.SalesThisMonth, true, trading => trading.Sales),
            (DashboardMetrics.ExpensesToday, false, trading => trading.Expenses),
            (DashboardMetrics.ExpensesThisMonth, true, trading => trading.Expenses),
            (DashboardMetrics.ProfitToday, false, trading => trading.Profit),
            (DashboardMetrics.ProfitThisMonth, true, trading => trading.Profit),
        ];

        if (!wanted.Any(metric => metricCodes.Contains(metric.Code)))
        {
            return;
        }

        var groups = await _context.AccountGroups
            .Where(group => group.FirmId == firmId)
            .Select(group => new { group.Id, group.Code, group.ParentId })
            .ToListAsync(cancellationToken);

        Dictionary<AccountGroupId, string> codes = groups.ToDictionary(group => group.Id, group => group.Code);
        Dictionary<string, AccountGroupId?> parents = groups.ToDictionary(
            group => group.Code, group => group.ParentId, StringComparer.Ordinal);

        bool Under(string groupCode, string headCode)
        {
            string? code = groupCode;

            for (int depth = 0; code is not null && depth < 20; depth++)
            {
                if (string.Equals(code, headCode, StringComparison.Ordinal))
                {
                    return true;
                }

                code = parents.TryGetValue(code, out AccountGroupId? parent) && parent is { } id
                    ? codes.GetValueOrDefault(id)
                    : null;
            }

            return false;
        }

        Trading today = Summarise(await _balances.GetMovementsAsync(firmId, asAt, asAt, cancellationToken));
        Trading month = Summarise(await _balances.GetMovementsAsync(
            firmId, new DateOnly(asAt.Year, asAt.Month, 1), asAt, cancellationToken));

        foreach ((string code, bool isMonth, Func<Trading, decimal> figure) in wanted)
        {
            if (metricCodes.Contains(code))
            {
                results[code] = new DashboardMetric(
                    Guid.Empty, code, figure(isMonth ? month : today), 0, [], IsPermitted: true);
            }
        }

        Trading Summarise(IReadOnlyList<LedgerMovement> movements)
        {
            decimal sales = 0m, expenses = 0m, income = 0m, spent = 0m;

            foreach (LedgerMovement movement in movements)
            {
                decimal debit = movement.PeriodDebit - movement.PeriodCredit;

                if (movement.Nature == AccountNature.Income)
                {
                    income -= debit;

                    if (Under(movement.GroupCode, SalesAccounts))
                    {
                        sales -= debit;
                    }
                }
                else if (movement.Nature == AccountNature.Expense)
                {
                    spent += debit;

                    if (!Under(movement.GroupCode, PurchaseAccounts))
                    {
                        expenses += debit;
                    }
                }
            }

            return new Trading(sales, expenses, income - spent);
        }
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

/// <summary>A period's trading in the three figures the dashboard shows.</summary>
/// <param name="Sales">Net sales.</param>
/// <param name="Expenses">Running expenses.</param>
/// <param name="Profit">Net profit.</param>
internal sealed record Trading(decimal Sales, decimal Expenses, decimal Profit);
