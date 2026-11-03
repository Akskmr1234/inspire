using System.Globalization;
using ERP.Application.Accounting.Reports;
using ERP.Application.Platform.Dashboards;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>Reads the charts under the dashboard's figures.</summary>
/// <remarks>
/// <para>
/// Built, like the figures, on what the reports read, so a chart and the report it
/// summarises always agree: the last thirty days, how this month was paid and its best
/// sellers from the sales register; purchases from the purchase register; sales and
/// profit by month from the same postings the profit and loss account totals, sorted
/// the way the trading tiles sort them, so this month's columns are those tiles.
/// </para>
/// <para>
/// The registers are read over a month or so, not a year: they go line by line, and a
/// year of a busy counter is hundreds of thousands of lines. The year comes from the
/// books, which hold it a ledger at a time, and from purchases added up where they are
/// kept, by the purchase register's own rules.
/// </para>
/// </remarks>
public sealed class DashboardChartReader : IDashboardChartReader
{
    /// <summary>How many days the daily sales cover.</summary>
    private const int RecentDays = 30;

    /// <summary>How many months the trend covers, this one included.</summary>
    private const int TrendMonths = 12;

    /// <summary>How many products or categories a ranking holds.</summary>
    private const int RankingSize = 8;

    private readonly ErpDbContext _context;
    private readonly ITransactionReportReader _reports;

    /// <summary>Initialises a new instance of the <see cref="DashboardChartReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="reports">The register reader, for the sales register.</param>
    public DashboardChartReader(ErpDbContext context, ITransactionReportReader reports)
    {
        _context = context;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<DashboardChartsResponse> ReadAsync(
        FirmId firmId,
        DateOnly asAt,
        DashboardChartAccess access,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        DateOnly monthStart = new(asAt.Year, asAt.Month, 1);
        DateOnly trendStart = monthStart.AddMonths(-(TrendMonths - 1));

        IReadOnlyList<DailySalesPoint>? daily = null;
        IReadOnlyList<PaymentModeShare>? modes = null;
        IReadOnlyList<RankedSales>? products = null;
        IReadOnlyList<RankedSales>? categories = null;

        if (access.Sales)
        {
            (daily, modes) = await ReadRecentSalesAsync(firmId, monthStart, asAt, cancellationToken);
            (products, categories) = await ReadBestSellersAsync(firmId, monthStart, asAt, cancellationToken);
        }

        Dictionary<string, decimal>? purchases = access.Purchases
            ? await ReadMonthlyPurchasesAsync(firmId, trendStart, asAt, cancellationToken)
            : null;

        Dictionary<string, Trading>? books = access.Accounts
            ? await ReadMonthlyTradingAsync(firmId, trendStart, asAt, cancellationToken)
            : null;

        List<MonthlyTradingPoint>? months = null;

        if (purchases is not null || books is not null)
        {
            months = [];

            // Every month in the window, the quiet ones too: a column left out would close
            // the gap and show a month without trading as no month at all.
            for (int offset = 0; offset < TrendMonths; offset++)
            {
                string key = MonthKey(trendStart.AddMonths(offset));
                Trading? trading = books?.GetValueOrDefault(key);
                decimal? sales = books is null ? null : trading?.Sales ?? 0m;
                decimal? bought = purchases is null ? null : purchases.GetValueOrDefault(key);
                decimal? profit = books is null ? null : trading?.Profit ?? 0m;

                months.Add(new MonthlyTradingPoint(key, sales, bought, profit));
            }
        }

        return new DashboardChartsResponse(
            asAt, string.Empty, daily, months, modes, products, categories, Receivables: null);
    }

    private static string MonthKey(DateOnly month) =>
        month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// The last thirty days' sales, day by day, and this month's by how they were paid -
    /// both from one read of the sales register, bill by bill.
    /// </summary>
    /// <remarks>
    /// Each bill's row is what the bill came to, tax, charges and rounding and all, and a
    /// return's is negative, so a day's or a payment mode's total is net of returns, as
    /// the day-wise register's is.
    /// </remarks>
    private async Task<(IReadOnlyList<DailySalesPoint> Daily, IReadOnlyList<PaymentModeShare> Modes)>
        ReadRecentSalesAsync(
            FirmId firmId,
            DateOnly monthStart,
            DateOnly asAt,
            CancellationToken cancellationToken)
    {
        DateOnly dailyStart = asAt.AddDays(-(RecentDays - 1));
        DateOnly from = dailyStart < monthStart ? dailyStart : monthStart;

        TransactionReport report = await _reports.ReadAsync(
            firmId,
            new GetTransactionReportQuery(
                TransactionReportDocument.Sales, from, asAt, TransactionReportGrouping.Bill),
            cancellationToken);

        List<SalesInvoiceId> ids =
        [
            .. report.Rows.Select(row => new SalesInvoiceId(Guid.Parse(row.Key, CultureInfo.InvariantCulture))),
        ];

        var bills = await _context.SalesInvoices.AsNoTracking()
            .Where(invoice => ids.Contains(invoice.Id))
            .Select(invoice => new { invoice.Id, invoice.Date, invoice.PaymentMode })
            .ToDictionaryAsync(invoice => invoice.Id.Value, cancellationToken);

        List<(DateOnly Date, SalesPaymentMode Mode, decimal Total)> sold =
        [
            .. report.Rows
                .Select(row => (Bill: Guid.Parse(row.Key, CultureInfo.InvariantCulture), row.Total))
                .Where(row => bills.ContainsKey(row.Bill))
                .Select(row => (bills[row.Bill].Date, bills[row.Bill].PaymentMode, row.Total)),
        ];

        ILookup<DateOnly, decimal> byDay = sold.ToLookup(bill => bill.Date, bill => bill.Total);
        List<DailySalesPoint> daily = [];

        // Every day, the quiet ones too, for the same reason a trend keeps its empty months.
        for (DateOnly day = dailyStart; day <= asAt; day = day.AddDays(1))
        {
            daily.Add(new DailySalesPoint(day, byDay[day].Sum(), byDay[day].Count()));
        }

        // In the order the modes are declared rather than by size, so each keeps its place
        // and its colour from one month to the next.
        List<PaymentModeShare> modes =
        [
            .. sold
                .Where(bill => bill.Date >= monthStart)
                .GroupBy(bill => bill.Mode)
                .OrderBy(group => group.Key)
                .Select(group => new PaymentModeShare(
                    group.Key, group.Sum(bill => bill.Total), group.Count())),
        ];

        return (daily, modes);
    }

    /// <summary>
    /// This month's best-selling products and categories, from one read of the sales
    /// register item by item: a category's sales are its products' sales.
    /// </summary>
    /// <remarks>
    /// Ranked by what they sold for, with tax, net of returns. The register leaves a
    /// bill's own charges off its item rows - they belong to no one item - so these do too.
    /// Something that took back more than it sold this month is no best seller, and is
    /// left out.
    /// </remarks>
    private async Task<(IReadOnlyList<RankedSales> Products, IReadOnlyList<RankedSales> Categories)>
        ReadBestSellersAsync(
            FirmId firmId,
            DateOnly from,
            DateOnly asAt,
            CancellationToken cancellationToken)
    {
        TransactionReport report = await _reports.ReadAsync(
            firmId,
            new GetTransactionReportQuery(
                TransactionReportDocument.Sales, from, asAt, TransactionReportGrouping.Item),
            cancellationToken);

        List<RankedSales> products =
        [
            .. report.Rows
                .Where(row => row.Total > 0m)
                .OrderByDescending(row => row.Total)
                .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
                .Take(RankingSize)
                .Select(row => new RankedSales(row.Label, row.Detail, row.Total, row.Quantity)),
        ];

        List<ProductId> productIds =
        [
            .. report.Rows.Select(row => new ProductId(Guid.Parse(row.Key, CultureInfo.InvariantCulture))),
        ];

        Dictionary<Guid, Guid> categoryOf = await _context.Products.AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(
                product => product.Id.Value, product => product.CategoryId.Value, cancellationToken);

        List<CategoryId> categoryIds = [.. categoryOf.Values.Distinct().Select(id => new CategoryId(id))];

        Dictionary<Guid, string> names = await _context.Categories.AsNoTracking()
            .Where(category => categoryIds.Contains(category.Id))
            .ToDictionaryAsync(category => category.Id.Value, category => category.Name, cancellationToken);

        List<RankedSales> categories =
        [
            .. report.Rows
                .Select(row => (
                    Category: categoryOf.GetValueOrDefault(Guid.Parse(row.Key, CultureInfo.InvariantCulture)),
                    row.Total,
                    row.Quantity))
                .Where(row => row.Category != Guid.Empty)
                .GroupBy(row => row.Category)
                .Select(group => new RankedSales(
                    names.GetValueOrDefault(group.Key) ?? "—",
                    null,
                    group.Sum(row => row.Total),
                    group.Sum(row => row.Quantity)))
                .Where(category => category.Amount > 0m)
                .OrderByDescending(category => category.Amount)
                .ThenBy(category => category.Label, StringComparer.OrdinalIgnoreCase)
                .Take(RankingSize),
        ];

        return (products, categories);
    }

    /// <summary>
    /// What suppliers billed each month for goods, before tax and net of returns - the
    /// purchase register's Amount column, month by month - which is what sales are in
    /// the books beside it.
    /// </summary>
    /// <remarks>
    /// Added up in the database rather than read through the register, which fetches a
    /// year's purchase lines one by one, with their charges and names, only for this to
    /// add them up. The rows are the register's own: posted bills within the period,
    /// each line's goods at their taxable value, a return's taken off, and the same
    /// joins to the products and suppliers the register makes, so a line it leaves out
    /// is left out here too. Rounded as the register rounds a row.
    /// </remarks>
    private async Task<Dictionary<string, decimal>> ReadMonthlyPurchasesAsync(
        FirmId firmId,
        DateOnly start,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var months = await (
                from line in _context.PurchaseInvoiceLines.AsNoTracking()
                join invoice in _context.PurchaseInvoices.AsNoTracking()
                    on line.PurchaseInvoiceId equals invoice.Id
                join product in _context.Products.AsNoTracking() on line.ProductId equals product.Id
                join supplier in _context.Ledgers.AsNoTracking() on invoice.SupplierLedgerId equals supplier.Id
                where invoice.FirmId == firmId
                    && invoice.Status == PurchaseInvoiceStatus.Posted
                    && invoice.Date >= start
                    && invoice.Date <= asAt
                group new
                {
                    Goods = invoice.Kind == PurchaseDocumentKind.Return
                        ? -line.TaxableAmount.Amount
                        : line.TaxableAmount.Amount,
                }
                by new { invoice.Date.Year, invoice.Date.Month } into month
                select new { month.Key.Year, month.Key.Month, Goods = month.Sum(row => row.Goods) })
            .ToListAsync(cancellationToken);

        return months.ToDictionary(
            month => MonthKey(new DateOnly(month.Year, month.Month, 1)),
            month => Math.Round(month.Goods, 2, MidpointRounding.AwayFromZero),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Sales and profit month by month, from the postings behind the profit and loss
    /// account.
    /// </summary>
    /// <remarks>
    /// The same postings the trading tiles read - posted vouchers dated within the
    /// period, at their base amounts, on the firm's ledgers - added up a month and a
    /// ledger at a time in one query rather than read again for each month.
    /// </remarks>
    private async Task<Dictionary<string, Trading>> ReadMonthlyTradingAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var postings = await _context.VoucherLines
            .Join(
                _context.Vouchers.Where(voucher =>
                    voucher.FirmId == firmId
                    && voucher.Status == VoucherStatus.Posted
                    && voucher.Date >= from
                    && voucher.Date <= asAt),
                line => line.VoucherId,
                voucher => voucher.Id,
                (line, voucher) => new
                {
                    voucher.Date.Year,
                    voucher.Date.Month,
                    line.LedgerId,
                    Debit = line.Side == EntrySide.Debit ? line.BaseAmount.Amount : -line.BaseAmount.Amount,
                })
            .GroupBy(row => new { row.Year, row.Month, row.LedgerId })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.LedgerId,
                Debit = group.Sum(row => row.Debit),
            })
            .ToListAsync(cancellationToken);

        var ledgers = await _context.Ledgers
            .Where(ledger => ledger.FirmId == firmId)
            .Join(
                _context.AccountGroups,
                ledger => ledger.AccountGroupId,
                group => group.Id,
                (ledger, group) => new { ledger.Id, GroupCode = group.Code, group.Nature })
            .ToDictionaryAsync(ledger => ledger.Id, cancellationToken);

        TradingClassifier classifier = await TradingClassifier.LoadAsync(_context, firmId, cancellationToken);

        return postings
            .Where(posting => ledgers.ContainsKey(posting.LedgerId))
            .GroupBy(posting => MonthKey(new DateOnly(posting.Year, posting.Month, 1)))
            .ToDictionary(
                month => month.Key,
                month => classifier.Summarise(month.Select(posting => (
                    ledgers[posting.LedgerId].GroupCode,
                    ledgers[posting.LedgerId].Nature,
                    posting.Debit))),
                StringComparer.Ordinal);
    }
}
