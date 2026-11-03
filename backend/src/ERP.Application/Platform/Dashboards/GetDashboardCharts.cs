using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Sales;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Dashboards;

/// <summary>The charts under a dashboard's figures: how trading has gone over time.</summary>
/// <param name="AsAt">The last day the charts cover. Defaults to today when omitted.</param>
/// <remarks>
/// <para>
/// Not panels of a dashboard but one fixed set of charts beside them, so every firm has
/// them without anybody arranging them, and nothing about a firm's dashboards changes.
/// </para>
/// <para>
/// Each chart is drawn from the report it summarises, and asked for with the permission
/// the dashboard's tiles ask for the same figures: the last thirty days and this month's
/// sales from the sales register, for whoever may open the sales bills; purchases from
/// the purchase register, for whoever may open the purchase bills; and sales and profit
/// by month and what customers owe from the books, for whoever may read the accounts. A
/// chart the caller may not read is left out rather than drawn empty - a flat line would
/// say there were no sales.
/// </para>
/// </remarks>
public sealed record GetDashboardChartsQuery(DateOnly? AsAt = null) : IQuery<DashboardChartsResponse>;

/// <summary>One day's sales.</summary>
/// <param name="Date">The day.</param>
/// <param name="Sales">What was billed, tax and charges and all, net of returns.</param>
/// <param name="Bills">How many bills.</param>
public sealed record DailySalesPoint(DateOnly Date, decimal Sales, int Bills);

/// <summary>One month's trading. A figure the caller may not read is null.</summary>
/// <param name="Month">The month, as <c>yyyy-MM</c>.</param>
/// <param name="Sales">
/// What was sold, before tax and net of returns: the sales accounts in the books, as the
/// sales tiles have them.
/// </param>
/// <param name="Purchases">What suppliers billed for goods, before tax and net of returns.</param>
/// <param name="Profit">Net profit: all income less all expense, as the profit and loss account has it.</param>
public sealed record MonthlyTradingPoint(string Month, decimal? Sales, decimal? Purchases, decimal? Profit);

/// <summary>How much of the period's sales was paid one way.</summary>
/// <param name="Mode">Cash, card, online or credit.</param>
/// <param name="Amount">What those bills came to, net of returns.</param>
/// <param name="Bills">How many bills.</param>
public sealed record PaymentModeShare(SalesPaymentMode Mode, decimal Amount, int Bills);

/// <summary>One entry of a ranking: a product or a category and what it sold.</summary>
/// <param name="Label">What it is called.</param>
/// <param name="Detail">A product's code; nothing for a category.</param>
/// <param name="Amount">What it sold for, with tax, net of returns.</param>
/// <param name="Quantity">How much of it went out, net of returns.</param>
public sealed record RankedSales(string Label, string? Detail, decimal Amount, decimal Quantity);

/// <summary>What customers owe, by how late it is: the age-wise debtors report's columns.</summary>
/// <param name="FromDays">The fewest days overdue the bucket holds: zero for what is not yet due.</param>
/// <param name="ToDays">The most, inclusive; null for the open-ended last bucket.</param>
/// <param name="Amount">What is owing in it.</param>
public sealed record ReceivableAge(int FromDays, int? ToDays, decimal Amount);

/// <summary>The charts, each null where the caller may not read the report behind it.</summary>
/// <param name="AsAt">The last day covered.</param>
/// <param name="Currency">The firm's base currency.</param>
/// <param name="DailySales">Sales on each of the last thirty days, quiet days included.</param>
/// <param name="Months">The last twelve months' sales, purchases and profit.</param>
/// <param name="PaymentModes">This month's sales by how they were paid.</param>
/// <param name="TopProducts">This month's best-selling products.</param>
/// <param name="TopCategories">This month's best-selling categories.</param>
/// <param name="Receivables">What customers owe, aged.</param>
public sealed record DashboardChartsResponse(
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<DailySalesPoint>? DailySales,
    IReadOnlyList<MonthlyTradingPoint>? Months,
    IReadOnlyList<PaymentModeShare>? PaymentModes,
    IReadOnlyList<RankedSales>? TopProducts,
    IReadOnlyList<RankedSales>? TopCategories,
    IReadOnlyList<ReceivableAge>? Receivables);

/// <summary>Which charts the caller may read, by the report each comes from.</summary>
/// <param name="Sales">The sales register: daily sales, payment modes and the rankings.</param>
/// <param name="Purchases">The purchase register: purchases by month.</param>
/// <param name="Accounts">
/// The books: sales and profit by month - as the sales and profit tiles read them - and
/// what customers owe, by age.
/// </param>
public sealed record DashboardChartAccess(bool Sales, bool Purchases, bool Accounts)
{
    /// <summary>Gets a value indicating whether there is anything at all to read.</summary>
    public bool Any => Sales || Purchases || Accounts;
}

/// <summary>Reads the charts' figures.</summary>
public interface IDashboardChartReader
{
    /// <summary>Reads the charts the caller may read.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="asAt">The last day covered.</param>
    /// <param name="access">Which charts to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The charts; one the caller may not read is null.</returns>
    Task<DashboardChartsResponse> ReadAsync(
        FirmId firmId,
        DateOnly asAt,
        DashboardChartAccess access,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetDashboardChartsQuery"/>.</summary>
public sealed class GetDashboardChartsQueryHandler
    : IQueryHandler<GetDashboardChartsQuery, DashboardChartsResponse>
{
    /// <summary>The permission code standing for every permission.</summary>
    private const string WildcardPermission = "*";

    private readonly IDashboardChartReader _charts;
    private readonly IOutstandingBillsReader _bills;
    private readonly IPermissionChecker _permissions;
    private readonly IFirmRepository _firms;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    /// <summary>Initialises a new instance of the <see cref="GetDashboardChartsQueryHandler"/> class.</summary>
    /// <param name="charts">The chart reader.</param>
    /// <param name="bills">The outstanding bills reader, for what customers owe.</param>
    /// <param name="permissions">The permission checker.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    public GetDashboardChartsQueryHandler(
        IDashboardChartReader charts,
        IOutstandingBillsReader bills,
        IPermissionChecker permissions,
        IFirmRepository firms,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IClock clock)
    {
        _charts = charts;
        _bills = bills;
        _permissions = permissions;
        _firms = firms;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<DashboardChartsResponse>> Handle(
        GetDashboardChartsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<DashboardChartsResponse>(firm.Error);
        }

        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<DashboardChartsResponse>(Error.Forbidden(
                "Dashboard.NotSignedIn", "A dashboard is shown to a signed-in user."));
        }

        IReadOnlySet<string> held = await _permissions.GetPermissionsAsync(
            _currentUser.UserId, cancellationToken);

        bool Holds(string permission) =>
            held.Contains(WildcardPermission) || held.Contains(permission);

        // As the dashboard's tiles have it: whoever may open the bills may see what they
        // add up to, and the books need the accounts' own permission.
        DashboardChartAccess access = new(
            Sales: Holds("sales:invoice:view"),
            Purchases: Holds("purchase:invoice:view"),
            Accounts: Holds("accounting:report:view"));

        DateOnly asAt = request.AsAt ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        string currency = firm.Value.BaseCurrency.Code;

        if (!access.Any)
        {
            return Result.Success(new DashboardChartsResponse(
                asAt, currency, null, null, null, null, null, null));
        }

        DashboardChartsResponse charts = await _charts.ReadAsync(
            firm.Value.Id, asAt, access, cancellationToken);

        IReadOnlyList<ReceivableAge>? receivables = access.Accounts
            ? await AgeReceivablesAsync(firm.Value.Id, asAt, cancellationToken)
            : null;

        return Result.Success(charts with { Currency = currency, Receivables = receivables });
    }

    /// <summary>
    /// What customers owe, cut as the age-wise debtors report cuts it: what is not yet
    /// due, then 1-30, 31-60, 61-90 and over 90 days past the due date.
    /// </summary>
    private async Task<IReadOnlyList<ReceivableAge>> AgeReceivablesAsync(
        FirmId firmId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OutstandingBillRow> rows = await _bills.ReadAsync(
            firmId, Domain.Accounting.BillType.Receivable, asAt, null, cancellationToken);

        IReadOnlyList<int> bounds = AgingBuckets.Default;
        decimal[] amounts = new decimal[bounds.Count + 2];

        foreach (OutstandingBillRow row in rows)
        {
            int late = GetOutstandingBillsQueryHandler.DaysOverdue(row.DueDate, asAt);
            int bucket = late == 0 ? 0 : 1 + bounds.Count(bound => late > bound);

            amounts[bucket] += row.OutstandingAmount;
        }

        List<ReceivableAge> ages = [new ReceivableAge(0, 0, amounts[0])];
        int from = 1;

        for (int index = 0; index < bounds.Count; index++)
        {
            ages.Add(new ReceivableAge(from, bounds[index], amounts[index + 1]));
            from = bounds[index] + 1;
        }

        ages.Add(new ReceivableAge(from, null, amounts[^1]));

        return ages;
    }
}
