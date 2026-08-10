using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Accounting.Cheques;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>
/// The cheque register: what happens to an instrument after the voucher that
/// recorded it.
/// </summary>
/// <remarks>
/// Cheques are brought into the register by the voucher that takes them in or writes
/// them out - see the <c>cheques</c> field on a voucher line. Everything here is a
/// later event: being banked, clearing, bouncing, being stopped. They are separate
/// endpoints because they are separate days.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/cheques")]
[Authorize]
[Produces("application/json")]
public sealed class ChequesController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="ChequesController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public ChequesController(ISender sender) => _sender = sender;

    /// <summary>Records a cheque being paid in, or presented against the firm's account.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">The account and date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Where the cheque now stands.</returns>
    /// <remarks>
    /// A cheque cannot be banked before the date on its face. That is the whole
    /// meaning of a post-dated cheque: presented early it comes back, and the firm
    /// pays a charge for the privilege.
    /// </remarks>
    /// <response code="200">Banked.</response>
    /// <response code="404">No such cheque or account in the selected firm.</response>
    /// <response code="422">It is not in hand, or its date has not arrived.</response>
    [HttpPost("{id:guid}/deposit")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(ChequeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DepositAsync(
        Guid id,
        [FromBody] DepositChequeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChequeStateResponse> result = await _sender.Send(
            new DepositChequeCommand(id, request.BankLedgerId, request.DepositedOn),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Records a cheque clearing.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">The date and the voucher posting the bank movement.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Where the cheque now stands.</returns>
    /// <remarks>
    /// The posting is supplied rather than generated. Which control account a cleared
    /// cheque moves out of is a firm's own choice of chart, and this is how bank
    /// reconciliation works anyway: post the statement, then mark off the cheques on
    /// it.
    /// </remarks>
    /// <response code="200">Cleared.</response>
    /// <response code="404">No such cheque or voucher in the selected firm.</response>
    /// <response code="422">It was never banked, or the voucher is not posted.</response>
    [HttpPost("{id:guid}/clear")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(ChequeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ClearAsync(
        Guid id,
        [FromBody] ClearChequeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChequeStateResponse> result = await _sender.Send(
            new ClearChequeCommand(id, request.ClearedOn, request.ClearingVoucherId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Records a cheque being dishonoured.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">The bank's reason and the date it was returned.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The cheque's new state and every bill the bounce put back.</returns>
    /// <remarks>
    /// The bills the original receipt settled are released automatically, in the same
    /// transaction, and listed in the response. The ledger postings are never raised
    /// here: which control account a bounced cheque comes back out of, and where the
    /// bank's charge goes, are a firm's own choice of chart. Name
    /// <c>reversalVoucherId</c> if the reversing journal has already been written, or
    /// attach it afterwards through <c>POST /cheques/{id}/reversal</c>. Until one is
    /// named the response returns <c>ledgerReversalRequired: true</c>, so a caller
    /// cannot mistake silence for completeness.
    /// </remarks>
    /// <response code="200">Bounced, with what was reopened.</response>
    /// <response code="404">No such cheque, or no such voucher, in the selected firm.</response>
    /// <response code="422">It is not with the bank, or the voucher cannot reverse it.</response>
    [HttpPost("{id:guid}/bounce")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(BouncedChequeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BounceAsync(
        Guid id,
        [FromBody] BounceChequeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<BouncedChequeResponse> result = await _sender.Send(
            new BounceChequeCommand(id, request.Reason, request.On, request.ReversalVoucherId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Names the voucher that reversed a bounced cheque's receipt.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">The voucher that took the receipt back out.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Where the cheque now stands.</returns>
    /// <remarks>
    /// The second half of a bounce, and the reason the first half reports that a
    /// reversal is owed. The voucher must be posted, belong to this firm, and touch the
    /// party the cheque came from - otherwise it cannot be the entry that undoes that
    /// receipt. The amount is deliberately not checked: a reversal usually carries the
    /// bank's charge alongside the cheque.
    /// </remarks>
    /// <response code="200">Recorded.</response>
    /// <response code="404">No such cheque, or no such voucher, in the selected firm.</response>
    /// <response code="422">It has not bounced, already names a reversal, or the voucher cannot be one.</response>
    [HttpPost("{id:guid}/reversal")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(ChequeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordReversalAsync(
        Guid id,
        [FromBody] RecordChequeReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChequeStateResponse> result = await _sender.Send(
            new RecordChequeReversalCommand(id, request.ReversalVoucherId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Records payment being stopped on a cheque the firm issued.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">Why, and from when.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Where the cheque now stands.</returns>
    /// <remarks>
    /// Issued cheques only. Only the drawer can stop a cheque, and for one the firm
    /// received that is the customer - from the firm's side it arrives as a bounce
    /// from the bank instead.
    /// </remarks>
    /// <response code="200">Stopped.</response>
    /// <response code="404">No such cheque in the selected firm.</response>
    /// <response code="422">It was received rather than issued, or has already closed.</response>
    [HttpPost("{id:guid}/stop")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(ChequeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> StopAsync(
        Guid id,
        [FromBody] CloseChequeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChequeStateResponse> result = await _sender.Send(
            new StopChequeCommand(id, request.Reason, request.On), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Voids a cheque that never reached a bank.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="request">Why, and from when.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Where the cheque now stands.</returns>
    /// <remarks>
    /// Only while it is still in hand. Once a cheque is in the banking system its
    /// outcome is the bank's to report, and voiding it here would leave the books
    /// saying nothing happened while the statement says otherwise.
    /// </remarks>
    /// <response code="200">Cancelled.</response>
    /// <response code="404">No such cheque in the selected firm.</response>
    /// <response code="422">It has already gone to the bank.</response>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("accounting", "cheque", "edit")]
    [ProducesResponseType(typeof(ChequeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] CloseChequeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<ChequeStateResponse> result = await _sender.Send(
            new CancelChequeCommand(id, request.Reason, request.On), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>Banking a cheque.</summary>
/// <param name="BankLedgerId">The firm's account it went through.</param>
/// <param name="DepositedOn">The date it was banked or presented.</param>
/// <remarks>
/// Both fields are marked required for the deserialiser. Omitted, a date would
/// otherwise arrive as 0001-01-01 and a ledger as the empty identifier - both
/// perfectly valid values that fail much later, and further from the mistake.
/// </remarks>
public sealed record DepositChequeRequest(
    [property: JsonRequired] Guid BankLedgerId,
    [property: JsonRequired] DateOnly DepositedOn);

/// <summary>Marking a cheque cleared.</summary>
/// <param name="ClearedOn">The date the funds moved.</param>
/// <param name="ClearingVoucherId">The voucher posting the bank movement.</param>
public sealed record ClearChequeRequest(
    [property: JsonRequired] DateOnly ClearedOn,
    [property: JsonRequired] Guid ClearingVoucherId);

/// <summary>Closing a cheque with a reason: bounced, stopped, or cancelled.</summary>
/// <param name="Reason">Why. Required, and recorded against the cheque.</param>
/// <param name="On">The date it took effect.</param>
/// <remarks>
/// One request shape for the three outcomes that need a reason. The outcome is the
/// route, so the body carries only what differs between calls - not a discriminator
/// the URL has already stated.
/// </remarks>
public sealed record CloseChequeRequest(
    string Reason,
    [property: JsonRequired] DateOnly On);

/// <summary>Recording a cheque being dishonoured.</summary>
/// <param name="Reason">The bank's reason for returning it. Required.</param>
/// <param name="On">The date it was returned.</param>
/// <param name="ReversalVoucherId">
/// The voucher taking the receipt back out of the books, where one has already been
/// written. Optional: the bank returns cheques to a cashier and the reversing journal
/// is usually written afterwards, so it can be attached later.
/// </param>
public sealed record BounceChequeRequest(
    string Reason,
    [property: JsonRequired] DateOnly On,
    Guid? ReversalVoucherId = null);

/// <summary>Naming the voucher that reversed a bounced cheque.</summary>
/// <param name="ReversalVoucherId">The voucher that took the receipt back out.</param>
public sealed record RecordChequeReversalRequest(
    [property: JsonRequired] Guid ReversalVoucherId);
