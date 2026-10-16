using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The charges each kind of document may carry, and which way each moves it.</summary>
/// <remarks>
/// Open to anybody signed in, as the settings are: whoever enters an invoice needs it to
/// add the invoice up, and it reveals nothing the invoice itself would not.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/accounting/charges")]
[Produces("application/json")]
public sealed class ChargesController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="ChargesController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public ChargesController(ISender sender) => _sender = sender;

    /// <summary>Lists the charges one kind of document may carry.</summary>
    /// <param name="document">The kind of document.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The charges, in the firm's order.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChargeSetup>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ChargeableDocument document,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ChargeSetup>> result = await _sender.Send(
            new ListChargesQuery(document), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
