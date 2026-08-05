using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
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
}
