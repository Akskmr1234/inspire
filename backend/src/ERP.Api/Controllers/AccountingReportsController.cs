using Asp.Versioning;
using ERP.Application.Accounting.Reports;
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
}
