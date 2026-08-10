using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Reports;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Ledgers - the posting accounts of the chart of accounts.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/ledgers")]
[Authorize]
[Produces("application/json")]
public sealed class LedgersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="LedgersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public LedgersController(ISender sender) => _sender = sender;

    /// <summary>Lists the selected firm's ledgers.</summary>
    /// <param name="activeOnly">Whether to exclude deactivated ledgers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ledgers, ordered by account group then ledger code.</returns>
    /// <remarks>
    /// Unpaged by design: a chart of accounts is a few hundred rows, and callers
    /// filter client-side as the user types.
    /// </remarks>
    /// <response code="200">The ledgers.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<LedgerSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<LedgerSummary>> result =
            await _sender.Send(new GetLedgersQuery(activeOnly), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads where a party stands against their credit limit.</summary>
    /// <param name="id">The party's ledger.</param>
    /// <param name="asAt">The date to state the position as at. Defaults to today.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What they owe, what they were allowed to owe, and what is left.</returns>
    /// <remarks>
    /// Read from the open bills rather than from the ledger balance. The two answer
    /// different questions: a balance includes payments on account and everything else
    /// posted to the party, while a credit limit is about invoices they have not paid.
    /// A customer who has paid in advance is not using credit, and a balance would say
    /// they were.
    /// <para>
    /// Nothing here refuses anything. A limit warns rather than blocks, so this reports
    /// the position and the document that asks decides what to do about it.
    /// </para>
    /// </remarks>
    /// <response code="200">The credit position.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpGet("{id:guid}/credit-status")]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(CreditStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCreditStatusAsync(
        Guid id,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<CreditStatus> result = await _sender.Send(
            new GetCreditStatusQuery(id, asAt), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
