using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>
/// Produces the account group report: the trial balance rolled up to the group each
/// ledger reports under.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="IncludeZeroBalances">
/// Whether to keep groups and ledgers with no opening balance and no movement.
/// </param>
/// <param name="IncludeLedgers">
/// Whether each group carries the ledgers behind its subtotal. On for a report that
/// drills down, off when only the group totals are wanted.
/// </param>
/// <remarks>
/// The same postings as the trial balance, summed a level up. An accountant reads it
/// to see where the money sits by category - Sundry Debtors against Sundry Creditors,
/// Direct Expenses against Indirect - without wading through several hundred ledgers,
/// and it is the shape the balance sheet's schedules are built from. Because it
/// re-aggregates the very figures the trial balance splits, the two reconcile to the
/// penny, and its column totals balance for the same reason the trial balance's do.
/// </remarks>
public sealed record GetAccountGroupSummaryQuery(
    DateOnly From,
    DateOnly To,
    bool IncludeZeroBalances = false,
    bool IncludeLedgers = true) : IQuery<AccountGroupSummaryResponse>;

/// <summary>One ledger's line beneath its group.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="OpeningDebit">Opening balance, when it is a debit.</param>
/// <param name="OpeningCredit">Opening balance, when it is a credit.</param>
/// <param name="PeriodDebit">Debits posted within the range.</param>
/// <param name="PeriodCredit">Credits posted within the range.</param>
/// <param name="ClosingDebit">Closing balance, when it is a debit.</param>
/// <param name="ClosingCredit">Closing balance, when it is a credit.</param>
public sealed record AccountGroupSummaryLedger(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit);

/// <summary>One group's subtotal on the account group report.</summary>
/// <param name="GroupCode">The group code.</param>
/// <param name="GroupName">The group name.</param>
/// <param name="Nature">Which side of the books the group sits on.</param>
/// <param name="OpeningDebit">Total opening debits of the ledgers beneath it.</param>
/// <param name="OpeningCredit">Total opening credits.</param>
/// <param name="PeriodDebit">Total debits posted in the range.</param>
/// <param name="PeriodCredit">Total credits posted in the range.</param>
/// <param name="ClosingDebit">Total closing debits.</param>
/// <param name="ClosingCredit">Total closing credits.</param>
/// <param name="LedgerCount">How many ledgers the subtotal covers.</param>
/// <param name="Ledgers">
/// The ledgers behind the subtotal, ordered by code, or empty when the report was
/// asked for totals only.
/// </param>
/// <remarks>
/// A group can carry both a debit and a credit subtotal at once, because it may hold
/// ledgers on either side - a Sundry Debtors group with one customer in credit is the
/// ordinary case. Each ledger's own closing is split into a single column first, so
/// the subtotals sum to the same grand totals the trial balance reports.
/// </remarks>
public sealed record AccountGroupSummaryRow(
    string GroupCode,
    string GroupName,
    AccountNature Nature,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit,
    int LedgerCount,
    IReadOnlyList<AccountGroupSummaryLedger> Ledgers);

/// <summary>The account group report.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Groups">
/// One row per group, ordered by nature then group code - assets first, expenses
/// last, as a set of financial statements reads.
/// </param>
/// <param name="TotalOpeningDebit">Total opening debits.</param>
/// <param name="TotalOpeningCredit">Total opening credits.</param>
/// <param name="TotalPeriodDebit">Total debits posted in the range.</param>
/// <param name="TotalPeriodCredit">Total credits posted in the range.</param>
/// <param name="TotalClosingDebit">Total closing debits.</param>
/// <param name="TotalClosingCredit">Total closing credits.</param>
/// <param name="IsBalanced">Whether debits equal credits in every column.</param>
/// <remarks>
/// <see cref="IsBalanced"/> carries the same weight it does on the trial balance: if
/// it is ever false the books are broken, and the report says so rather than printing
/// two totals that do not agree and leaving the reader to catch it.
/// </remarks>
public sealed record AccountGroupSummaryResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<AccountGroupSummaryRow> Groups,
    decimal TotalOpeningDebit,
    decimal TotalOpeningCredit,
    decimal TotalPeriodDebit,
    decimal TotalPeriodCredit,
    decimal TotalClosingDebit,
    decimal TotalClosingCredit,
    bool IsBalanced);

/// <summary>Validates a <see cref="GetAccountGroupSummaryQuery"/>.</summary>
public sealed class GetAccountGroupSummaryQueryValidator
    : AbstractValidator<GetAccountGroupSummaryQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetAccountGroupSummaryQueryValidator"/> class.</summary>
    public GetAccountGroupSummaryQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");
    }
}

/// <summary>Handles <see cref="GetAccountGroupSummaryQuery"/>.</summary>
/// <remarks>
/// Built on the trial balance's own reader rather than a second aggregation. The
/// group report is the trial balance grouped one level up, and giving it a separate
/// query over the ledger would be a second place for the arithmetic to drift from the
/// figures it is supposed to summarise.
/// </remarks>
public sealed class GetAccountGroupSummaryQueryHandler
    : IQueryHandler<GetAccountGroupSummaryQuery, AccountGroupSummaryResponse>
{
    private readonly ITrialBalanceReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetAccountGroupSummaryQueryHandler"/> class.</summary>
    /// <param name="reader">The trial balance aggregation reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetAccountGroupSummaryQueryHandler(
        ITrialBalanceReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<AccountGroupSummaryResponse>> Handle(
        GetAccountGroupSummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<AccountGroupSummaryResponse>(firm.Error);
        }

        IReadOnlyList<LedgerMovement> movements = await _reader.GetMovementsAsync(
            firm.Value.Id, request.From, request.To, cancellationToken);

        List<AccountGroupSummaryRow> groups = [];

        decimal totalOpeningDebit = 0m, totalOpeningCredit = 0m;
        decimal totalPeriodDebit = 0m, totalPeriodCredit = 0m;
        decimal totalClosingDebit = 0m, totalClosingCredit = 0m;

        // Grouped by the code the ledger reports under, then ordered the way a set of
        // financial statements reads: assets, liabilities, equity, income, expenses.
        // Ordering by nature before code keeps the balance sheet groups above the
        // profit and loss ones rather than interleaving them by an accident of coding.
        foreach (IGrouping<string, LedgerMovement> group in movements
            .GroupBy(m => m.GroupCode)
            .OrderBy(group => group.First().Nature)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            List<AccountGroupSummaryLedger> ledgers = [];

            decimal openingDebit = 0m, openingCredit = 0m;
            decimal periodDebit = 0m, periodCredit = 0m;
            decimal closingDebit = 0m, closingCredit = 0m;

            foreach (LedgerMovement movement in group
                .OrderBy(m => m.LedgerCode, StringComparer.Ordinal))
            {
                bool hasActivity =
                    movement.OpeningSigned != 0m
                    || movement.PeriodDebit != 0m
                    || movement.PeriodCredit != 0m;

                if (!hasActivity && !request.IncludeZeroBalances)
                {
                    continue;
                }

                // Each ledger's opening and closing are split into a single column
                // before they are summed, exactly as the trial balance does it, so the
                // group subtotals add up to the same grand totals and balance.
                decimal closingSigned =
                    movement.OpeningSigned + movement.PeriodDebit - movement.PeriodCredit;

                (decimal openDr, decimal openCr) = Split(movement.OpeningSigned);
                (decimal closeDr, decimal closeCr) = Split(closingSigned);

                openingDebit += openDr;
                openingCredit += openCr;
                periodDebit += movement.PeriodDebit;
                periodCredit += movement.PeriodCredit;
                closingDebit += closeDr;
                closingCredit += closeCr;

                if (request.IncludeLedgers)
                {
                    ledgers.Add(new AccountGroupSummaryLedger(
                        movement.LedgerId,
                        movement.LedgerCode,
                        movement.LedgerName,
                        openDr,
                        openCr,
                        movement.PeriodDebit,
                        movement.PeriodCredit,
                        closeDr,
                        closeCr));
                }
            }

            // A group every one of whose ledgers was filtered out contributes nothing
            // and is dropped, so the report does not carry empty headings.
            int ledgerCount = group.Count(m =>
                request.IncludeZeroBalances
                || m.OpeningSigned != 0m
                || m.PeriodDebit != 0m
                || m.PeriodCredit != 0m);

            if (ledgerCount == 0)
            {
                continue;
            }

            groups.Add(new AccountGroupSummaryRow(
                group.Key,
                group.First().GroupName,
                group.First().Nature,
                openingDebit,
                openingCredit,
                periodDebit,
                periodCredit,
                closingDebit,
                closingCredit,
                ledgerCount,
                ledgers));

            totalOpeningDebit += openingDebit;
            totalOpeningCredit += openingCredit;
            totalPeriodDebit += periodDebit;
            totalPeriodCredit += periodCredit;
            totalClosingDebit += closingDebit;
            totalClosingCredit += closingCredit;
        }

        bool isBalanced =
            totalOpeningDebit == totalOpeningCredit
            && totalPeriodDebit == totalPeriodCredit
            && totalClosingDebit == totalClosingCredit;

        return Result.Success(new AccountGroupSummaryResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            groups,
            totalOpeningDebit,
            totalOpeningCredit,
            totalPeriodDebit,
            totalPeriodCredit,
            totalClosingDebit,
            totalClosingCredit,
            isBalanced));
    }

    /// <summary>Splits a debit-positive figure into debit and credit columns.</summary>
    /// <param name="signed">The signed amount.</param>
    /// <returns>The debit and credit values, exactly one of which is non-zero.</returns>
    private static (decimal Debit, decimal Credit) Split(decimal signed) =>
        signed >= 0m ? (signed, 0m) : (0m, -signed);
}
