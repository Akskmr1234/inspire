using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>Produces a trial balance for a date range.</summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <param name="IncludeZeroBalances">
/// Whether to list ledgers with no opening balance and no movement.
/// </param>
public sealed record GetTrialBalanceQuery(
    DateOnly From,
    DateOnly To,
    bool IncludeZeroBalances = false) : IQuery<TrialBalanceResponse>;

/// <summary>One ledger's line on a trial balance.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="GroupCode">The code of the group it reports under.</param>
/// <param name="GroupName">The name of the group it reports under.</param>
/// <param name="Nature">Which side of the books the group sits on.</param>
/// <param name="OpeningDebit">Opening balance, when it is a debit.</param>
/// <param name="OpeningCredit">Opening balance, when it is a credit.</param>
/// <param name="PeriodDebit">Debits posted within the range.</param>
/// <param name="PeriodCredit">Credits posted within the range.</param>
/// <param name="ClosingDebit">Closing balance, when it is a debit.</param>
/// <param name="ClosingCredit">Closing balance, when it is a credit.</param>
/// <remarks>
/// Each balance appears as a debit column or a credit column, never as a signed
/// number, because that is how a trial balance is read and printed. The internal
/// arithmetic uses a single debit-positive figure and splits it at the end.
/// </remarks>
public sealed record TrialBalanceRow(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string GroupCode,
    string GroupName,
    AccountNature Nature,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit);

/// <summary>A trial balance.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Rows">One row per ledger, ordered by group then ledger code.</param>
/// <param name="TotalOpeningDebit">Total opening debits.</param>
/// <param name="TotalOpeningCredit">Total opening credits.</param>
/// <param name="TotalPeriodDebit">Total debits posted in the range.</param>
/// <param name="TotalPeriodCredit">Total credits posted in the range.</param>
/// <param name="TotalClosingDebit">Total closing debits.</param>
/// <param name="TotalClosingCredit">Total closing credits.</param>
/// <param name="IsBalanced">
/// Whether debits equal credits in every column.
/// </param>
/// <remarks>
/// <see cref="IsBalanced"/> is the point of the whole report. If it is ever false
/// the books are broken, and the report says so rather than quietly printing two
/// different totals and leaving the reader to notice.
/// </remarks>
public sealed record TrialBalanceResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalOpeningDebit,
    decimal TotalOpeningCredit,
    decimal TotalPeriodDebit,
    decimal TotalPeriodCredit,
    decimal TotalClosingDebit,
    decimal TotalClosingCredit,
    bool IsBalanced);

/// <summary>Validates a <see cref="GetTrialBalanceQuery"/>.</summary>
public sealed class GetTrialBalanceQueryValidator : AbstractValidator<GetTrialBalanceQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetTrialBalanceQueryValidator"/> class.</summary>
    public GetTrialBalanceQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");
    }
}

/// <summary>
/// Reads the aggregated figures a trial balance is built from.
/// </summary>
/// <remarks>
/// A dedicated read abstraction rather than the aggregate repositories. A trial
/// balance is a grouped aggregation over every posting in a firm; loading vouchers
/// as aggregates to sum them in memory would materialise the entire ledger to
/// produce a few dozen rows. The implementation aggregates in the database.
/// </remarks>
public interface ITrialBalanceReader
{
    /// <summary>Aggregates postings per ledger for a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per ledger that has an opening balance or any movement.</returns>
    Task<IReadOnlyList<LedgerMovement>> GetMovementsAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>A ledger's opening position and movement within a period.</summary>
/// <param name="LedgerId">The ledger.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="GroupCode">The group code.</param>
/// <param name="GroupName">The group name.</param>
/// <param name="Nature">The group's nature.</param>
/// <param name="OpeningSigned">
/// The opening balance in debit-positive terms: the ledger's own opening balance
/// plus every posting dated before the period.
/// </param>
/// <param name="PeriodDebit">Debits posted within the period.</param>
/// <param name="PeriodCredit">Credits posted within the period.</param>
public sealed record LedgerMovement(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    string GroupCode,
    string GroupName,
    AccountNature Nature,
    decimal OpeningSigned,
    decimal PeriodDebit,
    decimal PeriodCredit);

/// <summary>Handles <see cref="GetTrialBalanceQuery"/>.</summary>
public sealed class GetTrialBalanceQueryHandler
    : IQueryHandler<GetTrialBalanceQuery, TrialBalanceResponse>
{
    private readonly ITrialBalanceReader _reader;
    private readonly Abstractions.Persistence.IFirmRepository _firms;
    private readonly Abstractions.Tenancy.ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetTrialBalanceQueryHandler"/> class.</summary>
    /// <param name="reader">The aggregation reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetTrialBalanceQueryHandler(
        ITrialBalanceReader reader,
        Abstractions.Persistence.IFirmRepository firms,
        Abstractions.Tenancy.ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<TrialBalanceResponse>> Handle(
        GetTrialBalanceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<TrialBalanceResponse>(Error.Forbidden(
                "Report.NoFirmSelected", "A firm must be selected to run this report."));
        }

        Domain.Tenancy.Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<TrialBalanceResponse>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        IReadOnlyList<LedgerMovement> movements = await _reader.GetMovementsAsync(
            firmId, request.From, request.To, cancellationToken);

        List<TrialBalanceRow> rows = [];

        decimal openingDebit = 0m, openingCredit = 0m;
        decimal periodDebit = 0m, periodCredit = 0m;
        decimal closingDebit = 0m, closingCredit = 0m;

        foreach (LedgerMovement movement in movements)
        {
            // Closing is derived from the signed opening plus the period's net
            // movement, then split into columns once. Splitting first and adding
            // the columns separately would produce a debit and a credit on the same
            // ledger, which is not what a trial balance shows.
            decimal closingSigned =
                movement.OpeningSigned + movement.PeriodDebit - movement.PeriodCredit;

            bool hasActivity =
                movement.OpeningSigned != 0m
                || movement.PeriodDebit != 0m
                || movement.PeriodCredit != 0m;

            if (!hasActivity && !request.IncludeZeroBalances)
            {
                continue;
            }

            (decimal openDr, decimal openCr) = Split(movement.OpeningSigned);
            (decimal closeDr, decimal closeCr) = Split(closingSigned);

            rows.Add(new TrialBalanceRow(
                movement.LedgerId,
                movement.LedgerCode,
                movement.LedgerName,
                movement.GroupCode,
                movement.GroupName,
                movement.Nature,
                openDr,
                openCr,
                movement.PeriodDebit,
                movement.PeriodCredit,
                closeDr,
                closeCr));

            openingDebit += openDr;
            openingCredit += openCr;
            periodDebit += movement.PeriodDebit;
            periodCredit += movement.PeriodCredit;
            closingDebit += closeDr;
            closingCredit += closeCr;
        }

        bool isBalanced =
            openingDebit == openingCredit
            && periodDebit == periodCredit
            && closingDebit == closingCredit;

        return Result.Success(new TrialBalanceResponse(
            request.From,
            request.To,
            firm.BaseCurrency.Code,
            rows,
            openingDebit,
            openingCredit,
            periodDebit,
            periodCredit,
            closingDebit,
            closingCredit,
            isBalanced));
    }

    /// <summary>Splits a debit-positive figure into debit and credit columns.</summary>
    /// <param name="signed">The signed amount.</param>
    /// <returns>The debit and credit values, exactly one of which is non-zero.</returns>
    private static (decimal Debit, decimal Credit) Split(decimal signed) =>
        signed >= 0m ? (signed, 0m) : (0m, -signed);
}
