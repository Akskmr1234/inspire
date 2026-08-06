using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>Which activity a movement of cash belongs to.</summary>
/// <remarks>
/// The three headings every cash flow statement is presented under. Which one a
/// movement falls to is decided by the account on the other side of the posting -
/// see <see cref="CashFlowClassification"/>, where the rule is stated in one place
/// rather than inferred at each call site.
/// </remarks>
public enum CashFlowCategory
{
    /// <summary>Trading: sales, purchases, expenses, and the debtors and creditors behind them.</summary>
    Operating = 1,

    /// <summary>Buying and selling the assets the business trades with rather than trades in.</summary>
    Investing = 2,

    /// <summary>Capital and borrowings: money from owners and lenders, and returns to them.</summary>
    Financing = 3,
}

/// <summary>
/// Produces the cash flow statement: what actually moved through cash and bank over
/// a period, and what it was for.
/// </summary>
/// <param name="From">The first date included, inclusive.</param>
/// <param name="To">The last date included, inclusive.</param>
/// <remarks>
/// <para>
/// The direct method, built from the postings themselves rather than reconstructed
/// backwards from profit. Every voucher touching a cash or bank account contributes
/// the accounts on its other side, and those accounts say what the money was for.
/// </para>
/// <para>
/// A transfer between two of the firm's own accounts contributes nothing, which is
/// correct and falls out of the method rather than needing to be special-cased: such
/// a voucher has no non-cash line, and the firm's cash position is unchanged by moving
/// money from the till to the bank.
/// </para>
/// </remarks>
public sealed record GetCashFlowQuery(DateOnly From, DateOnly To)
    : IQuery<CashFlowResponse>;

/// <summary>One account's contribution to the cash flow statement.</summary>
/// <param name="LedgerId">The account the money came from or went to.</param>
/// <param name="LedgerCode">Its code.</param>
/// <param name="LedgerName">Its name.</param>
/// <param name="Inflow">Cash received against this account.</param>
/// <param name="Outflow">Cash paid against it.</param>
/// <param name="Net">Inflow less outflow.</param>
/// <remarks>
/// Inflow and outflow are both carried rather than netted, because an account with
/// half a million in each direction and nothing net is a materially different fact
/// from one with no activity at all.
/// </remarks>
public sealed record CashFlowLine(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    decimal Inflow,
    decimal Outflow,
    decimal Net);

/// <summary>One section of the statement.</summary>
/// <param name="Category">Operating, investing, or financing.</param>
/// <param name="Lines">The accounts contributing, largest net movement first.</param>
/// <param name="Inflow">Total received under this heading.</param>
/// <param name="Outflow">Total paid under it.</param>
/// <param name="Net">The heading's net contribution to the cash position.</param>
public sealed record CashFlowSection(
    CashFlowCategory Category,
    IReadOnlyList<CashFlowLine> Lines,
    decimal Inflow,
    decimal Outflow,
    decimal Net);

/// <summary>The cash flow statement.</summary>
/// <param name="From">The first date included.</param>
/// <param name="To">The last date included.</param>
/// <param name="Currency">The base currency the figures are stated in.</param>
/// <param name="Sections">Operating, investing, and financing, in that order.</param>
/// <param name="OpeningBalance">Cash and bank in hand at the start of the period.</param>
/// <param name="ClosingBalance">Cash and bank in hand at the end of it.</param>
/// <param name="NetChange">The sum of the three sections.</param>
/// <param name="IsReconciled">
/// Whether opening plus the classified movement equals closing.
/// </param>
/// <remarks>
/// <see cref="IsReconciled"/> is what makes the statement trustworthy, and it carries
/// the same weight as the trial balance's own balance check. A cash flow statement is
/// a claim about where a known change in the cash position came from; if the parts do
/// not add back to that change, something moved through the bank that the statement
/// has not accounted for, and the right response is to say so rather than to print
/// three plausible sections that quietly do not sum.
/// </remarks>
public sealed record CashFlowResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<CashFlowSection> Sections,
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal NetChange,
    bool IsReconciled);

/// <summary>Validates a <see cref="GetCashFlowQuery"/>.</summary>
public sealed class GetCashFlowQueryValidator : AbstractValidator<GetCashFlowQuery>
{
    /// <summary>The longest period the statement will cover in one request.</summary>
    public const int MaximumRangeDays = 366;

    /// <summary>Initialises a new instance of the <see cref="GetCashFlowQueryValidator"/> class.</summary>
    public GetCashFlowQueryValidator()
    {
        RuleFor(q => q.From).NotEqual(default(DateOnly));
        RuleFor(q => q.To).NotEqual(default(DateOnly));

        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From)
            .WithMessage("The end of the range cannot precede its start.");

        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaximumRangeDays)
            .WithMessage($"A cash flow statement cannot span more than {MaximumRangeDays} days.")
            .When(q => q.To >= q.From);
    }
}

/// <summary>Decides which heading an account's cash movement is reported under.</summary>
/// <remarks>
/// <para>
/// Stated once, here, because it is the one piece of judgement in the report and it
/// must be identical everywhere it is applied. The rule reads in order:
/// </para>
/// <list type="number">
/// <item><description>
/// A customer or supplier account is <b>operating</b>. This is tested before nature
/// and it has to be: a debtor is an asset and a creditor a liability, so classifying
/// by nature alone would file ordinary trading receipts under investing and payments
/// to suppliers under financing.
/// </description></item>
/// <item><description>
/// Income and expense accounts are <b>operating</b> - what the business does.
/// </description></item>
/// <item><description>
/// Equity and remaining liability accounts are <b>financing</b>: capital introduced,
/// drawings, loans taken and repaid.
/// </description></item>
/// <item><description>
/// Remaining asset accounts are <b>investing</b>: plant, equipment, investments.
/// </description></item>
/// </list>
/// <para>
/// This is a defensible default rather than a statement of the firm's own accounting
/// policy. A business with, say, a long-term deposit filed under current assets would
/// see it reported as investing, which may or may not be what its auditor expects.
/// Making it configurable needs a per-firm mapping that does not exist yet, and
/// inventing one would be guessing at somebody's chart of accounts.
/// </para>
/// </remarks>
public static class CashFlowClassification
{
    /// <summary>Classifies an account.</summary>
    /// <param name="kind">What the ledger represents.</param>
    /// <param name="nature">Which side of the books its group sits on.</param>
    /// <returns>The heading the movement belongs under.</returns>
    public static CashFlowCategory Classify(LedgerKind kind, AccountNature nature)
    {
        if (kind is LedgerKind.Customer or LedgerKind.Supplier)
        {
            return CashFlowCategory.Operating;
        }

        return nature switch
        {
            AccountNature.Income or AccountNature.Expense => CashFlowCategory.Operating,
            AccountNature.Equity or AccountNature.Liability => CashFlowCategory.Financing,
            _ => CashFlowCategory.Investing,
        };
    }
}

/// <summary>
/// One account's cash movement over the period, before it is classified.
/// </summary>
/// <param name="LedgerId">The account on the other side of the cash posting.</param>
/// <param name="LedgerCode">Its code.</param>
/// <param name="LedgerName">Its name.</param>
/// <param name="Kind">What it represents.</param>
/// <param name="Nature">Which side of the books its group sits on.</param>
/// <param name="Inflow">Cash received against it.</param>
/// <param name="Outflow">Cash paid against it.</param>
public sealed record CashFlowMovement(
    Guid LedgerId,
    string LedgerCode,
    string LedgerName,
    LedgerKind Kind,
    AccountNature Nature,
    decimal Inflow,
    decimal Outflow);

/// <summary>The cash position and the movements that changed it.</summary>
/// <param name="OpeningBalance">Cash and bank in hand at the start of the period.</param>
/// <param name="ClosingBalance">Cash and bank in hand at the end of it.</param>
/// <param name="Movements">One entry per account posted against cash in the period.</param>
public sealed record CashFlowData(
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<CashFlowMovement> Movements);

/// <summary>Reads the cash position and the postings that moved it.</summary>
public interface ICashFlowReader
{
    /// <summary>Reads the cash flow of a period.</summary>
    /// <param name="firmId">The firm, so another firm's cash cannot be read.</param>
    /// <param name="from">The first date of the period.</param>
    /// <param name="to">The last date of the period.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The opening and closing positions, and the movements between them.</returns>
    Task<CashFlowData> ReadAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetCashFlowQuery"/>.</summary>
public sealed class GetCashFlowQueryHandler
    : IQueryHandler<GetCashFlowQuery, CashFlowResponse>
{
    private readonly ICashFlowReader _reader;
    private readonly IFirmRepository _firms;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetCashFlowQueryHandler"/> class.</summary>
    /// <param name="reader">The cash flow reader.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetCashFlowQueryHandler(
        ICashFlowReader reader,
        IFirmRepository firms,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _firms = firms;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<CashFlowResponse>> Handle(
        GetCashFlowQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Domain.Tenancy.Firm> firm = await StatementContext.ResolveFirmAsync(
            _firms, _tenantContext, cancellationToken);

        if (firm.IsFailure)
        {
            return Result.Failure<CashFlowResponse>(firm.Error);
        }

        CashFlowData data = await _reader.ReadAsync(
            firm.Value.Id, request.From, request.To, cancellationToken);

        List<CashFlowSection> sections = [];
        decimal netChange = 0m;

        // Every heading is emitted, including an empty one. A cash flow statement with
        // no investing section reads as though the report forgot to look; one showing
        // investing at nil states plainly that nothing was bought or sold.
        CashFlowCategory[] headings =
        [
            CashFlowCategory.Operating,
            CashFlowCategory.Investing,
            CashFlowCategory.Financing,
        ];

        foreach (CashFlowCategory category in headings)
        {
            List<CashFlowLine> lines = [];

            decimal inflow = 0m;
            decimal outflow = 0m;

            foreach (CashFlowMovement movement in data.Movements
                .Where(m => CashFlowClassification.Classify(m.Kind, m.Nature) == category)
                // Largest mover first: a reader scanning a section wants the account
                // that explains most of it, not the one whose code sorts first.
                .OrderByDescending(m => Math.Abs(m.Inflow - m.Outflow))
                .ThenBy(m => m.LedgerCode, StringComparer.Ordinal))
            {
                lines.Add(new CashFlowLine(
                    movement.LedgerId,
                    movement.LedgerCode,
                    movement.LedgerName,
                    movement.Inflow,
                    movement.Outflow,
                    movement.Inflow - movement.Outflow));

                inflow += movement.Inflow;
                outflow += movement.Outflow;
            }

            sections.Add(new CashFlowSection(
                category, lines, inflow, outflow, inflow - outflow));

            netChange += inflow - outflow;
        }

        return Result.Success(new CashFlowResponse(
            request.From,
            request.To,
            firm.Value.BaseCurrency.Code,
            sections,
            data.OpeningBalance,
            data.ClosingBalance,
            netChange,
            data.OpeningBalance + netChange == data.ClosingBalance));
    }
}
