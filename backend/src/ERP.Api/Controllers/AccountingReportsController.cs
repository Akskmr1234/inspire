using Asp.Versioning;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Accounting reports.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/reports")]
[Authorize]
[Produces("application/json")]
public sealed class AccountingReportsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="AccountingReportsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public AccountingReportsController(ISender sender) => _sender = sender;

    /// <summary>Produces a trial balance for a date range.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="includeZeroBalances">Whether to list ledgers with no activity.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The trial balance.</returns>
    /// <remarks>
    /// Figures are stated in the firm's base currency and cover posted vouchers
    /// only - drafts are not in the books, and cancelled vouchers have been reversed
    /// out. The response carries <c>isBalanced</c>; if it is ever false the books are
    /// broken and the caller should say so rather than present the numbers.
    /// </remarks>
    /// <response code="200">One row per ledger, with column totals and a balance check.</response>
    /// <response code="400">The date range is invalid.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("trial-balance")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(TrialBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTrialBalanceAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeZeroBalances,
        CancellationToken cancellationToken)
    {
        Result<TrialBalanceResponse> result = await _sender.Send(
            new GetTrialBalanceQuery(from, to, includeZeroBalances), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces a profit and loss statement for a date range.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Income, expenses, and the net result for the period.</returns>
    /// <remarks>
    /// Period figures, not cumulative: this answers what the business earned between
    /// the two dates.
    /// </remarks>
    /// <response code="200">Income and expense lines with the net result.</response>
    /// <response code="400">The date range is invalid.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("profit-and-loss")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(ProfitAndLossResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProfitAndLossAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        Result<ProfitAndLossResponse> result = await _sender.Send(
            new GetProfitAndLossQuery(from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces a balance sheet as at a date.</summary>
    /// <param name="asAt">The date the position is stated as at, inclusive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Assets against liabilities and equity.</returns>
    /// <remarks>
    /// Cumulative figures, and the retained result of every income and expense
    /// posting is carried into equity - without it the statement would be out by
    /// exactly the period's profit.
    /// </remarks>
    /// <response code="200">Assets, liabilities, equity, and a balance check.</response>
    /// <response code="400">The date is missing.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("balance-sheet")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(BalanceSheetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBalanceSheetAsync(
        [FromQuery] DateOnly asAt,
        CancellationToken cancellationToken)
    {
        Result<BalanceSheetResponse> result = await _sender.Send(
            new GetBalanceSheetQuery(asAt), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces a statement of account for one ledger.</summary>
    /// <param name="ledgerId">The ledger to report on.</param>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The postings with a running balance.</returns>
    /// <remarks>
    /// Carries the balance brought forward, a running balance per line, and the
    /// contra ledgers for each entry - the "particulars" an accountant reads to see
    /// what a movement was for.
    /// </remarks>
    /// <response code="200">The statement.</response>
    /// <response code="400">The range or ledger is invalid.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpGet("ledger-statement/{ledgerId:guid}")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(LedgerStatementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLedgerStatementAsync(
        Guid ledgerId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        Result<LedgerStatementResponse> result = await _sender.Send(
            new GetLedgerStatementQuery(ledgerId, from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the day book: every voucher posted in a date range.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="voucherType">
    /// Restricts the register to one kind of voucher. Omit for all kinds; pass
    /// <c>CashReceipt</c> and <c>CashPayment</c> style values to narrow it.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The vouchers of the period, oldest first, each with its lines.</returns>
    /// <remarks>
    /// The chronological register of what the firm actually did, as opposed to
    /// where it stands. Posted vouchers only, so it reconciles with the trial
    /// balance. <c>totalDebit</c> and <c>totalCredit</c> are both returned and must
    /// agree; if they ever differ, something has posted incorrectly and the caller
    /// should say so rather than present the figures.
    /// </remarks>
    /// <response code="200">The register, with period totals and a voucher count.</response>
    /// <response code="400">The date range is invalid, or spans more than a year.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("day-book")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(DayBookResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDayBookAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] VoucherType? voucherType,
        CancellationToken cancellationToken)
    {
        Result<DayBookResponse> result = await _sender.Send(
            new GetDayBookQuery(from, to, voucherType), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the voucher report: a register of vouchers by document.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="type">Restricts the register to one kind of voucher. Omit for all.</param>
    /// <param name="status">
    /// Restricts to one status. Omit for all - showing drafts and cancelled vouchers,
    /// which the posted-only day book cannot, is the register's reason to exist beside
    /// it.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One row per voucher, most recent first, with its value.</returns>
    /// <remarks>
    /// Read document by document rather than line by line: what an operator opens to
    /// find a particular voucher, across every status rather than the posted ones the
    /// day book is limited to. Amounts are shown in each voucher's own currency and
    /// totalled in the base currency.
    /// </remarks>
    /// <response code="200">The register, with a count by status and a base-currency total.</response>
    /// <response code="400">The date range is invalid, or spans more than a year.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("voucher-report")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(VoucherReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVoucherReportAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] VoucherType? type,
        [FromQuery] VoucherStatus? status,
        CancellationToken cancellationToken)
    {
        Result<VoucherReportResponse> result = await _sender.Send(
            new GetVoucherReportQuery(from, to, type, status), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the cash flow statement for a date range.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Operating, investing, and financing movements, with the cash position.</returns>
    /// <remarks>
    /// The direct method: built from the postings themselves rather than reconstructed
    /// backwards from profit. A transfer between the firm's own accounts contributes
    /// nothing, since it does not change what the firm holds.
    /// <para>
    /// The response carries <c>isReconciled</c>: whether the opening position plus the
    /// classified movement equals the closing position. If it is ever false, something
    /// moved through the bank the statement has not accounted for, and the caller
    /// should say so rather than present three sections that do not sum.
    /// </para>
    /// </remarks>
    /// <response code="200">The statement, with a reconciliation check.</response>
    /// <response code="400">The date range is invalid, or spans more than a year.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("cash-flow")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(CashFlowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCashFlowAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        Result<CashFlowResponse> result = await _sender.Send(
            new GetCashFlowQuery(from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the transaction summary: activity in totals, by type and month.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="status">Restricts the summary to one status. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Totals per voucher type and per calendar month.</returns>
    /// <remarks>
    /// No individual voucher appears: just how many of each kind were raised and what
    /// they came to. The control total an auditor ticks against, and the shape of a
    /// period's activity before deciding which report to open next. Figures are stated
    /// in the base currency, converted at the rate each voucher was posted with.
    /// </remarks>
    /// <response code="200">Totals by type and by month, with a count by status.</response>
    /// <response code="400">The date range is invalid, or spans more than three years.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("transaction-summary")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(TransactionSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTransactionSummaryAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] VoucherStatus? status,
        CancellationToken cancellationToken)
    {
        Result<TransactionSummaryResponse> result = await _sender.Send(
            new GetTransactionSummaryQuery(from, to, status), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the cash book: movement on every cash account.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="ledgerId">Restricts the report to one till. Omit for all of them.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One section per cash account, with a running balance.</returns>
    /// <remarks>
    /// Figures are labelled receipts and payments rather than debits and credits:
    /// cash is an asset, so a debit is money arriving, and that is how the people
    /// who read a cash book think about it.
    /// </remarks>
    /// <response code="200">The cash book.</response>
    /// <response code="400">The date range is invalid, or spans more than a year.</response>
    /// <response code="404">A ledger was named that is not a cash account of this firm.</response>
    [HttpGet("cash-book")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(CashBankBookResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetCashBookAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken) =>
        SendCashBankBookAsync(from, to, LedgerKind.Cash, ledgerId, cancellationToken);

    /// <summary>Produces the bank book: movement on every bank account.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="ledgerId">Restricts the report to one account. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One section per bank account, with a running balance.</returns>
    /// <response code="200">The bank book.</response>
    /// <response code="400">The date range is invalid, or spans more than a year.</response>
    /// <response code="404">A ledger was named that is not a bank account of this firm.</response>
    [HttpGet("bank-book")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(CashBankBookResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetBankBookAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken) =>
        SendCashBankBookAsync(from, to, LedgerKind.Bank, ledgerId, cancellationToken);

    /// <summary>Produces the account group report: the trial balance rolled up by group.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="includeZeroBalances">Whether to keep groups and ledgers with no activity.</param>
    /// <param name="includeLedgers">
    /// Whether each group carries the ledgers behind its subtotal, for drill-down.
    /// Defaults to <c>true</c>; pass <c>false</c> for group totals alone.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One row per group, with opening, period, and closing columns.</returns>
    /// <remarks>
    /// The same postings as the trial balance, summed a level up, so the two reconcile.
    /// The response carries <c>isBalanced</c>; if it is ever false the books are broken
    /// and the caller should say so rather than present the figures.
    /// </remarks>
    /// <response code="200">The group report, with per-group subtotals and column totals.</response>
    /// <response code="400">The date range is invalid.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("account-group-summary")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(AccountGroupSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAccountGroupSummaryAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeZeroBalances,
        [FromQuery] bool? includeLedgers,
        CancellationToken cancellationToken)
    {
        Result<AccountGroupSummaryResponse> result = await _sender.Send(
            new GetAccountGroupSummaryQuery(
                from, to, includeZeroBalances, includeLedgers ?? true),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Produces the debtors report: what customers still owe, bill by bill.</summary>
    /// <param name="asAt">The date the position is stated as at.</param>
    /// <param name="ledgerId">Restricts the report to one customer. Omit for all.</param>
    /// <param name="overdueOnly">Restricts it to bills already past their due date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One section per customer, with their open bills.</returns>
    /// <remarks>
    /// Historically accurate: bills raised after <c>asAt</c> are excluded and receipts
    /// made after it are ignored, so re-running the report for a past period end gives
    /// the figures that were printed at the time rather than today's.
    /// <para>
    /// Bills are stated in the currency they were raised in. The response carries
    /// <c>currencies</c>; where it holds more than one entry the totals are a sum
    /// across currencies and should not be presented as a single figure.
    /// </para>
    /// </remarks>
    /// <response code="200">The debtors report.</response>
    /// <response code="400">The date is missing.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("debtors")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(OutstandingBillsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetDebtorsAsync(
        [FromQuery] DateOnly asAt,
        [FromQuery] Guid? ledgerId,
        [FromQuery] bool overdueOnly,
        CancellationToken cancellationToken) =>
        SendOutstandingAsync(
            BillType.Receivable, asAt, ledgerId, overdueOnly, cancellationToken);

    /// <summary>Produces the creditors report: what the firm still owes suppliers.</summary>
    /// <param name="asAt">The date the position is stated as at.</param>
    /// <param name="ledgerId">Restricts the report to one supplier. Omit for all.</param>
    /// <param name="overdueOnly">Restricts it to bills already past their due date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One section per supplier, with their open bills.</returns>
    /// <response code="200">The creditors report.</response>
    /// <response code="400">The date is missing.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("creditors")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(OutstandingBillsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetCreditorsAsync(
        [FromQuery] DateOnly asAt,
        [FromQuery] Guid? ledgerId,
        [FromQuery] bool overdueOnly,
        CancellationToken cancellationToken) =>
        SendOutstandingAsync(BillType.Payable, asAt, ledgerId, overdueOnly, cancellationToken);

    /// <summary>Ages the debtors into buckets.</summary>
    /// <param name="asAt">The date the position is aged as at.</param>
    /// <param name="bucketDays">
    /// The upper bound of each bucket, in days overdue, ascending. Omit for
    /// 30/60/90, which gives 1-30, 31-60, 61-90, and over 90.
    /// </param>
    /// <param name="ledgerId">Restricts the report to one customer. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One row per customer, with an amount per bucket.</returns>
    /// <remarks>
    /// Bills not yet due are reported in their own column rather than in the first
    /// bucket. An aging report exists to separate what is late from what is merely
    /// owed, and folding the two together defeats the point of running it.
    /// </remarks>
    /// <response code="200">The age-wise debtors report.</response>
    /// <response code="400">The date is missing, or the buckets do not ascend.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("debtors-aging")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(AgingAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetDebtorsAgingAsync(
        [FromQuery] DateOnly asAt,
        [FromQuery] int[]? bucketDays,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken) =>
        SendAgingAsync(BillType.Receivable, asAt, bucketDays, ledgerId, cancellationToken);

    /// <summary>Ages the creditors into buckets.</summary>
    /// <param name="asAt">The date the position is aged as at.</param>
    /// <param name="bucketDays">
    /// The upper bound of each bucket, in days overdue, ascending. Omit for 30/60/90.
    /// </param>
    /// <param name="ledgerId">Restricts the report to one supplier. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One row per supplier, with an amount per bucket.</returns>
    /// <response code="200">The age-wise creditors report.</response>
    /// <response code="400">The date is missing, or the buckets do not ascend.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("creditors-aging")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(AgingAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetCreditorsAgingAsync(
        [FromQuery] DateOnly asAt,
        [FromQuery] int[]? bucketDays,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken) =>
        SendAgingAsync(BillType.Payable, asAt, bucketDays, ledgerId, cancellationToken);

    /// <summary>Lists the post-dated cheques still in hand: the PDC report.</summary>
    /// <param name="asAt">The date the position is stated as at.</param>
    /// <param name="direction">
    /// <c>Received</c> for cheques the firm holds, <c>Issued</c> for those it has
    /// written and not yet seen presented. Omit for both.
    /// </param>
    /// <param name="ledgerId">Restricts the report to one party. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The pending cheques dated after the reporting date, soonest due first.</returns>
    /// <remarks>
    /// Only cheques still in hand appear: one already with the bank is no longer a
    /// promise but an outcome waiting to be reported, and belongs on the register. A
    /// cheque counts as post-dated only while its own date is still in the future
    /// relative to <c>asAt</c>.
    /// </remarks>
    /// <response code="200">The pending post-dated cheques, with receivable and payable totals.</response>
    /// <response code="400">The date is missing, or the direction is not recognised.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("post-dated-cheques")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(PostDatedChequesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPostDatedChequesAsync(
        [FromQuery] DateOnly asAt,
        [FromQuery] ChequeDirection? direction,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken)
    {
        Result<PostDatedChequesResponse> result = await _sender.Send(
            new GetPostDatedChequesQuery(asAt, direction, ledgerId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Groups pending cheques by the day they fall due: the PDC calendar.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="direction">Received, issued, or both.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The days on which something falls due, in date order, with a net figure.</returns>
    /// <remarks>
    /// The same cheques as the PDC report, arranged by date rather than by party,
    /// because the question is different: not who owes what but what lands this week
    /// and whether the account can carry it. Only days with cheques on them appear.
    /// </remarks>
    /// <response code="200">The calendar, with receivable and payable totals over the period.</response>
    /// <response code="400">The range is invalid, or spans more than two years.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("cheque-calendar")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(ChequeCalendarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetChequeCalendarAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] ChequeDirection? direction,
        CancellationToken cancellationToken)
    {
        Result<ChequeCalendarResponse> result = await _sender.Send(
            new GetChequeCalendarQuery(from, to, direction), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Lists every cheque taken in or written out over a period: the register.</summary>
    /// <param name="from">The first date included, inclusive.</param>
    /// <param name="to">The last date included, inclusive.</param>
    /// <param name="direction">Received, issued, or both.</param>
    /// <param name="status">Restricts to one lifecycle status. Omit for all.</param>
    /// <param name="ledgerId">Restricts to one party. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The cheques of the period, most recently taken in first.</returns>
    /// <remarks>
    /// Read by when a cheque changed hands, not by when it falls due, and it shows
    /// closed cheques as well as live ones - a register that dropped a cheque the
    /// moment it cleared could not answer the question it is usually opened for.
    /// </remarks>
    /// <response code="200">The register, with totals taken in and written out, and a count by status.</response>
    /// <response code="400">The range is invalid, or spans more than two years.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet("cheque-register")]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(ChequeRegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetChequeRegisterAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] ChequeDirection? direction,
        [FromQuery] ChequeStatus? status,
        [FromQuery] Guid? ledgerId,
        CancellationToken cancellationToken)
    {
        Result<ChequeRegisterResponse> result = await _sender.Send(
            new GetChequeRegisterQuery(from, to, direction, status, ledgerId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Dispatches the shared outstanding query.</summary>
    /// <param name="type">Receivable for debtors, payable for creditors.</param>
    /// <param name="asAt">The date the position is stated as at.</param>
    /// <param name="ledgerId">The single party to restrict to, if any.</param>
    /// <param name="overdueOnly">Whether to list only bills past their due date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    /// <remarks>
    /// Debtors and creditors are one report over the opposite kind of bill. Separate
    /// routes because that is how they are asked for, one implementation so the
    /// arithmetic cannot drift between them.
    /// </remarks>
    private async Task<IActionResult> SendOutstandingAsync(
        BillType type,
        DateOnly asAt,
        Guid? ledgerId,
        bool overdueOnly,
        CancellationToken cancellationToken)
    {
        Result<OutstandingBillsResponse> result = await _sender.Send(
            new GetOutstandingBillsQuery(type, asAt, ledgerId, overdueOnly), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Dispatches the shared aging query.</summary>
    /// <param name="type">Receivable for debtors, payable for creditors.</param>
    /// <param name="asAt">The date the position is aged as at.</param>
    /// <param name="bucketDays">The bucket boundaries, or null for the default.</param>
    /// <param name="ledgerId">The single party to restrict to, if any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    private async Task<IActionResult> SendAgingAsync(
        BillType type,
        DateOnly asAt,
        int[]? bucketDays,
        Guid? ledgerId,
        CancellationToken cancellationToken)
    {
        Result<AgingAnalysisResponse> result = await _sender.Send(
            new GetAgingAnalysisQuery(
                type,
                asAt,
                bucketDays is { Length: > 0 } ? bucketDays : null,
                ledgerId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Dispatches the shared cash/bank book query.
    /// </summary>
    /// <param name="from">The first date included.</param>
    /// <param name="to">The last date included.</param>
    /// <param name="kind">Which book to produce.</param>
    /// <param name="ledgerId">The single account to restrict to, if any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    /// <remarks>
    /// The two books are one report over a different set of accounts. They get
    /// separate routes because that is how an accountant asks for them, but only
    /// one implementation, so the arithmetic cannot drift between them.
    /// </remarks>
    private async Task<IActionResult> SendCashBankBookAsync(
        DateOnly from,
        DateOnly to,
        LedgerKind kind,
        Guid? ledgerId,
        CancellationToken cancellationToken)
    {
        Result<CashBankBookResponse> result = await _sender.Send(
            new GetCashBankBookQuery(from, to, kind, ledgerId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
