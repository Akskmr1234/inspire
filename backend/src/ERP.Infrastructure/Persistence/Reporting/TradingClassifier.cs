using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Reporting;

/// <summary>
/// Sorts ledger movements into the trading figures the dashboard shows - sales, running
/// expenses and profit - the way the profit and loss account sorts them.
/// </summary>
/// <remarks>
/// <para>
/// Stock is perpetual - a sale posts its cost of goods as it goes - so income less
/// expense over a period is the period's profit without any closing-stock adjustment.
/// </para>
/// <para>
/// Sales are the income ledgers under Sales Accounts, net of returns. Expenses are
/// the running costs: every expense ledger except those under Purchase Accounts,
/// where the cost of goods sold sits. A shopkeeper asking what was spent today means
/// rent, wages and electricity, not the cost of what went out of the door.
/// </para>
/// <para>
/// One for the tiles and the charts alike, so the month's column on a chart is the
/// month's figure on its tile.
/// </para>
/// </remarks>
internal sealed class TradingClassifier
{
    /// <summary>The group the sales ledgers sit under.</summary>
    private const string SalesAccounts = "4100";

    /// <summary>The group the purchase and cost-of-goods ledgers sit under.</summary>
    private const string PurchaseAccounts = "5100";

    /// <summary>How far up the chart of accounts a group is followed, so a loop in it cannot hang a read.</summary>
    private const int MaximumDepth = 20;

    private readonly Dictionary<AccountGroupId, string> _codes;
    private readonly Dictionary<string, AccountGroupId?> _parents;

    private TradingClassifier(
        Dictionary<AccountGroupId, string> codes,
        Dictionary<string, AccountGroupId?> parents)
    {
        _codes = codes;
        _parents = parents;
    }

    /// <summary>Reads a firm's chart of accounts to sort its ledgers by.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The classifier.</returns>
    public static async Task<TradingClassifier> LoadAsync(
        ErpDbContext context,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        var groups = await context.AccountGroups
            .Where(group => group.FirmId == firmId)
            .Select(group => new { group.Id, group.Code, group.ParentId })
            .ToListAsync(cancellationToken);

        return new TradingClassifier(
            groups.ToDictionary(group => group.Id, group => group.Code),
            groups.ToDictionary(group => group.Code, group => group.ParentId, StringComparer.Ordinal));
    }

    /// <summary>Totals a period's ledger movements into its trading figures.</summary>
    /// <param name="movements">Each ledger's movement within the period.</param>
    /// <returns>The period's trading.</returns>
    public Trading Summarise(IEnumerable<LedgerMovement> movements) =>
        Summarise(movements.Select(movement => (
            movement.GroupCode,
            movement.Nature,
            movement.PeriodDebit - movement.PeriodCredit)));

    /// <summary>Totals what was posted to each ledger over a period into its trading figures.</summary>
    /// <param name="movements">Each ledger's group, its nature, and its debits less its credits.</param>
    /// <returns>The period's trading.</returns>
    public Trading Summarise(IEnumerable<(string GroupCode, AccountNature Nature, decimal Debit)> movements)
    {
        decimal sales = 0m, expenses = 0m, income = 0m, spent = 0m;

        foreach ((string groupCode, AccountNature nature, decimal debit) in movements)
        {
            if (nature == AccountNature.Income)
            {
                income -= debit;

                if (Under(groupCode, SalesAccounts))
                {
                    sales -= debit;
                }
            }
            else if (nature == AccountNature.Expense)
            {
                spent += debit;

                if (!Under(groupCode, PurchaseAccounts))
                {
                    expenses += debit;
                }
            }
        }

        return new Trading(sales, expenses, income - spent);
    }

    /// <summary>Whether a group is a head group or sits anywhere beneath it.</summary>
    private bool Under(string groupCode, string headCode)
    {
        string? code = groupCode;

        for (int depth = 0; code is not null && depth < MaximumDepth; depth++)
        {
            if (string.Equals(code, headCode, StringComparison.Ordinal))
            {
                return true;
            }

            code = _parents.TryGetValue(code, out AccountGroupId? parent) && parent is { } id
                ? _codes.GetValueOrDefault(id)
                : null;
        }

        return false;
    }
}

/// <summary>A period's trading in the three figures the dashboard shows.</summary>
/// <param name="Sales">Net sales.</param>
/// <param name="Expenses">Running expenses.</param>
/// <param name="Profit">Net profit.</param>
internal sealed record Trading(decimal Sales, decimal Expenses, decimal Profit);
