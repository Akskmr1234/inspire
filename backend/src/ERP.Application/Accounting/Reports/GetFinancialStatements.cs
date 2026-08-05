using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

// ---------------------------------------------------------------- profit and loss

/// <summary>Produces a profit and loss statement for a date range.</summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
public sealed record GetProfitAndLossQuery(DateOnly From, DateOnly To)
    : IQuery<ProfitAndLossResponse>;

/// <summary>One line of a financial statement.</summary>
/// <param name="GroupCode">The account group's code.</param>
/// <param name="GroupName">The account group's name.</param>
/// <param name="LedgerCode">The ledger code.</param>
/// <param name="LedgerName">The ledger name.</param>
/// <param name="Amount">
/// The amount, always presented positive in the sense the statement reads - revenue
/// as a positive figure, cost as a positive figure.
/// </param>
public sealed record StatementLine(
    string GroupCode,
    string GroupName,
    string LedgerCode,
    string LedgerName,
    decimal Amount);

/// <summary>A profit and loss statement.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Income">Income lines.</param>
/// <param name="Expenses">Expense lines.</param>
/// <param name="TotalIncome">Total income for the period.</param>
/// <param name="TotalExpenses">Total expenses for the period.</param>
/// <param name="NetProfit">
/// Income less expenses. Negative when the period made a loss.
/// </param>
public sealed record ProfitAndLossResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<StatementLine> Income,
    IReadOnlyList<StatementLine> Expenses,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetProfit);

/// <summary>Validates a <see cref="GetProfitAndLossQuery"/>.</summary>
public sealed class GetProfitAndLossQueryValidator : AbstractValidator<GetProfitAndLossQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetProfitAndLossQueryValidator"/> class.</summary>
    public GetProfitAndLossQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");
    }
}

/// <summary>Handles <see cref="GetProfitAndLossQuery"/>.</summary>
public sealed class GetProfitAndLossQueryHandler
    : IQueryHandler<GetProfitAndLossQuery, ProfitAndLossResponse>
{
    private readonly ITrialBalanceReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetProfitAndLossQueryHandler"/> class.</summary>
    /// <param name="reader">The aggregation reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetProfitAndLossQueryHandler(
        ITrialBalanceReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Income and expenses are <em>period</em> figures, never cumulative. A profit
    /// and loss statement answers "what did the business earn between these dates";
    /// carrying a balance forward the way the balance sheet does would restate every
    /// prior period's result as though it happened again.
    /// </remarks>
    public async Task<Result<ProfitAndLossResponse>> Handle(
        GetProfitAndLossQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<ProfitAndLossResponse>(firm.Error);
        }

        IReadOnlyList<LedgerMovement> movements = await _reader.GetMovementsAsync(
            firm.Value.Id, request.From, request.To, cancellationToken);

        List<StatementLine> income = [];
        List<StatementLine> expenses = [];
        decimal totalIncome = 0m, totalExpenses = 0m;

        foreach (LedgerMovement movement in movements)
        {
            decimal periodSigned = movement.PeriodDebit - movement.PeriodCredit;

            if (periodSigned == 0m)
            {
                continue;
            }

            switch (movement.Nature)
            {
                case AccountNature.Income:
                    // Income accumulates on the credit side, so a debit-positive net
                    // is negative. Negating presents revenue the way the statement
                    // reads.
                    income.Add(Line(movement, -periodSigned));
                    totalIncome += -periodSigned;
                    break;

                case AccountNature.Expense:
                    expenses.Add(Line(movement, periodSigned));
                    totalExpenses += periodSigned;
                    break;

                default:
                    // Assets, liabilities, and equity belong on the balance sheet.
                    break;
            }
        }

        return Result.Success(new ProfitAndLossResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            income,
            expenses,
            totalIncome,
            totalExpenses,
            totalIncome - totalExpenses));
    }

    private static StatementLine Line(LedgerMovement movement, decimal amount) => new(
        movement.GroupCode, movement.GroupName, movement.LedgerCode, movement.LedgerName, amount);
}

// ---------------------------------------------------------------- balance sheet

/// <summary>Produces a balance sheet as at a date.</summary>
/// <param name="AsAt">The date the position is stated as at, inclusive.</param>
public sealed record GetBalanceSheetQuery(DateOnly AsAt) : IQuery<BalanceSheetResponse>;

/// <summary>A balance sheet.</summary>
/// <param name="AsAt">The date the position is stated as at.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Assets">Asset lines.</param>
/// <param name="Liabilities">Liability lines.</param>
/// <param name="Equity">Equity lines.</param>
/// <param name="TotalAssets">Total assets.</param>
/// <param name="TotalLiabilities">Total liabilities.</param>
/// <param name="TotalEquity">Total equity, excluding the retained result.</param>
/// <param name="RetainedEarnings">
/// The cumulative result of every income and expense posting up to
/// <paramref name="AsAt"/>.
/// </param>
/// <param name="TotalLiabilitiesAndEquity">
/// Liabilities plus equity plus <paramref name="RetainedEarnings"/>.
/// </param>
/// <param name="IsBalanced">Whether assets equal liabilities plus equity.</param>
public sealed record BalanceSheetResponse(
    DateOnly AsAt,
    string Currency,
    IReadOnlyList<StatementLine> Assets,
    IReadOnlyList<StatementLine> Liabilities,
    IReadOnlyList<StatementLine> Equity,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal RetainedEarnings,
    decimal TotalLiabilitiesAndEquity,
    bool IsBalanced);

/// <summary>Validates a <see cref="GetBalanceSheetQuery"/>.</summary>
public sealed class GetBalanceSheetQueryValidator : AbstractValidator<GetBalanceSheetQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetBalanceSheetQueryValidator"/> class.</summary>
    public GetBalanceSheetQueryValidator() =>
        RuleFor(q => q.AsAt).NotEqual(default(DateOnly));
}

/// <summary>Handles <see cref="GetBalanceSheetQuery"/>.</summary>
public sealed class GetBalanceSheetQueryHandler
    : IQueryHandler<GetBalanceSheetQuery, BalanceSheetResponse>
{
    private readonly ITrialBalanceReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetBalanceSheetQueryHandler"/> class.</summary>
    /// <param name="reader">The aggregation reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetBalanceSheetQueryHandler(
        ITrialBalanceReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The balance sheet balances only because the retained result is carried into
    /// equity. Assets, liabilities, and equity alone do not agree: the income and
    /// expense accounts hold the difference, and a statement that omitted them would
    /// be out by exactly the period's profit. That is the single most common way a
    /// hand-built balance sheet is wrong.
    /// </para>
    /// <para>
    /// It follows from the trial balance identity. Every posted line sums to zero in
    /// debit-positive terms, so
    /// <c>assets = -(liabilities + equity + income + expenses)</c>, and negating the
    /// income and expense pair gives exactly the retained result.
    /// </para>
    /// </remarks>
    public async Task<Result<BalanceSheetResponse>> Handle(
        GetBalanceSheetQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<BalanceSheetResponse>(firm.Error);
        }

        // Asking for a single day makes the reader's "everything before the period"
        // figure the cumulative position, which is precisely what a balance sheet
        // states. No extra query shape is needed.
        IReadOnlyList<LedgerMovement> movements = await _reader.GetMovementsAsync(
            firm.Value.Id, request.AsAt, request.AsAt, cancellationToken);

        List<StatementLine> assets = [];
        List<StatementLine> liabilities = [];
        List<StatementLine> equity = [];

        decimal totalAssets = 0m, totalLiabilities = 0m, totalEquity = 0m;
        decimal retained = 0m;

        foreach (LedgerMovement movement in movements)
        {
            decimal closingSigned =
                movement.OpeningSigned + movement.PeriodDebit - movement.PeriodCredit;

            if (closingSigned == 0m)
            {
                continue;
            }

            switch (movement.Nature)
            {
                case AccountNature.Asset:
                    assets.Add(Line(movement, closingSigned));
                    totalAssets += closingSigned;
                    break;

                case AccountNature.Liability:
                    liabilities.Add(Line(movement, -closingSigned));
                    totalLiabilities += -closingSigned;
                    break;

                case AccountNature.Equity:
                    equity.Add(Line(movement, -closingSigned));
                    totalEquity += -closingSigned;
                    break;

                case AccountNature.Income:
                case AccountNature.Expense:
                    // Rolled into the retained result rather than listed. Their
                    // detail belongs on the profit and loss statement.
                    retained += -closingSigned;
                    break;

                default:
                    break;
            }
        }

        decimal totalLiabilitiesAndEquity = totalLiabilities + totalEquity + retained;

        return Result.Success(new BalanceSheetResponse(
            request.AsAt,
            firm.Value.BaseCurrency.Code,
            assets,
            liabilities,
            equity,
            totalAssets,
            totalLiabilities,
            totalEquity,
            retained,
            totalLiabilitiesAndEquity,
            totalAssets == totalLiabilitiesAndEquity));
    }

    private static StatementLine Line(LedgerMovement movement, decimal amount) => new(
        movement.GroupCode, movement.GroupName, movement.LedgerCode, movement.LedgerName, amount);
}

/// <summary>Shared setup for the financial statements.</summary>
internal static class StatementContext
{
    /// <summary>Resolves the firm a statement is being run for.</summary>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The firm, or the reason it could not be resolved.</returns>
    internal static async Task<Result<Firm>> ResolveFirmAsync(
        IFirmRepository firms,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Firm>(Error.Forbidden(
                "Report.NoFirmSelected", "A firm must be selected to run this report."));
        }

        Firm? firm = await firms.FindAsync(firmId, cancellationToken);

        return firm is null
            ? Result.Failure<Firm>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."))
            : Result.Success(firm);
    }
}
