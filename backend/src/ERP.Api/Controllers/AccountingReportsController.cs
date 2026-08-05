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
