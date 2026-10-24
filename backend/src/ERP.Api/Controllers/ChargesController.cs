using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
using ERP.Domain.Accounting;
using ERP.Identity.Authorization;
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

    /// <summary>Sets a ledger up as a charge one kind of document may carry.</summary>
    /// <param name="request">The document, the ledger and which way it moves the total.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The charge as the entry screens read it.</returns>
    /// <response code="200">The charge, new or as it was already set up.</response>
    /// <response code="400">The ledger cannot be a charge: a party, cash, bank or tax account.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpPost]
    [RequiresPermission("accounting", "additional-ledger", "create")]
    [ProducesResponseType(typeof(ChargeSetup), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MapAsync(
        [FromBody] MapChargeCommand request,
        CancellationToken cancellationToken)
    {
        Result<ChargeSetup> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Takes a ledger off the charges one kind of document carries.</summary>
    /// <param name="document">The kind of document.</param>
    /// <param name="ledgerId">The ledger.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Withdrawn; documents already carrying it keep it.</response>
    /// <response code="404">The ledger is not set up for that kind of document.</response>
    [HttpDelete("{document}/{ledgerId:guid}")]
    [RequiresPermission("accounting", "additional-ledger", "create")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WithdrawAsync(
        ChargeableDocument document,
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new WithdrawChargeCommand(document, ledgerId), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Marks a charge as one every new document of its kind starts with, or not.</summary>
    /// <param name="document">The kind of document.</param>
    /// <param name="ledgerId">The ledger.</param>
    /// <param name="request">Whether it is a default.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The charge as the entry screens read it.</returns>
    /// <response code="200">The charge, with its new setting.</response>
    /// <response code="404">The ledger is not set up for that kind of document.</response>
    [HttpPut("{document}/{ledgerId:guid}/default")]
    [RequiresPermission("accounting", "additional-ledger", "create")]
    [ProducesResponseType(typeof(ChargeSetup), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDefaultAsync(
        ChargeableDocument document,
        Guid ledgerId,
        [FromBody] ChargeDefaultRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChargeSetup> result = await _sender.Send(
            new SetChargeDefaultCommand(document, ledgerId, request.IsDefault), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>Whether a charge is one every new document starts with.</summary>
/// <param name="IsDefault">Whether it appears by itself.</param>
public sealed record ChargeDefaultRequest([property: JsonRequired] bool IsDefault);
