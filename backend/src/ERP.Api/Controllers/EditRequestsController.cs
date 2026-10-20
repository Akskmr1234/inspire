using System.Text.Json;
using Asp.Versioning;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Platform.Approvals;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>
/// Changes to posted documents held for approval: listed for the people who may decide
/// them, made when approved, and left undone when refused or withdrawn.
/// </summary>
/// <remarks>
/// Behind sign-in only. Who sees and decides what is settled per request from the
/// permission to approve that kind of document, so one screen serves the sales approver,
/// the purchase approver and the accountant, each seeing their own.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/edit-requests")]
[Authorize]
[Produces("application/json")]
public sealed class EditRequestsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="EditRequestsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public EditRequestsController(ISender sender) => _sender = sender;

    /// <summary>Lists the requests the user may decide, and those they made.</summary>
    /// <param name="status">Only those standing here.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The requests, newest first.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EditRequestSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] EditRequestStatus? status,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<EditRequestSummary>> result = await _sender.Send(
            new ListEditRequestsQuery(status), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one request with the change it holds.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EditRequestDetail), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<EditRequestDetail> result = await _sender.Send(new GetEditRequestQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Approves a request: the change is made exactly as it was asked for.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">A note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// The change goes through the same path, and the same checks, as if the approver had
    /// made it. Where it is refused now - the invoice has since been paid against, say - the
    /// request stays waiting and the reason is returned, so the approver can refuse it.
    /// </remarks>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody] EditDecisionRequest? request,
        CancellationToken cancellationToken)
    {
        Result<EditRequestDetail> found = await _sender.Send(new GetEditRequestQuery(id), cancellationToken);

        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        if (!found.Value.Summary.CanDecide)
        {
            return Problem(Error.Forbidden(
                "EditRequest.MayNotDecide", "You may not approve this change, or it has already been decided."));
        }

        Result made = await MakeAsync(found.Value, cancellationToken);

        if (made.IsFailure)
        {
            return Problem(made.Error);
        }

        Result decided = await _sender.Send(
            new DecideEditRequestCommand(id, EditRequestStatus.Approved, request?.Note), cancellationToken);

        return decided.IsSuccess ? NoContent() : Problem(decided.Error);
    }

    /// <summary>Refuses a request; the document stands as it was.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        [FromBody] EditDecisionRequest? request,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new DecideEditRequestCommand(id, EditRequestStatus.Rejected, request?.Note), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a request the user made.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> WithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new DecideEditRequestCommand(id, EditRequestStatus.Withdrawn, null), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    private static T Read<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, ApprovalJson)
        ?? throw new InvalidOperationException("A change held for approval could not be read back.");

    private async Task<Result> MakeAsync(EditRequestDetail held, CancellationToken cancellationToken)
    {
        EditRequestSummary request = held.Summary;
        Guid document = request.DocumentId;

        return (request.Kind, request.Action) switch
        {
            (EditDocumentKind.SalesInvoice, EditAction.Edit) => await _sender.Send(
                SalesController.EditCommand(document, Read<UpdateSalesInvoiceRequest>(held.Payload)),
                cancellationToken),
            (EditDocumentKind.SalesInvoice, _) => await _sender.Send(
                new CancelSalesInvoiceCommand(document, Read<CancelSalesInvoiceRequest>(held.Payload).Reason),
                cancellationToken),
            (EditDocumentKind.PurchaseInvoice, EditAction.Edit) => await _sender.Send(
                PurchaseController.EditCommand(document, Read<UpdatePurchaseInvoiceRequest>(held.Payload)),
                cancellationToken),
            (EditDocumentKind.PurchaseInvoice, _) => await _sender.Send(
                new CancelPurchaseInvoiceCommand(document, Read<CancelPurchaseInvoiceRequest>(held.Payload).Reason),
                cancellationToken),
            (EditDocumentKind.Voucher, EditAction.Edit) => await _sender.Send(
                VouchersController.EditCommand(document, Read<UpdateVoucherRequest>(held.Payload)),
                cancellationToken),
            _ => await _sender.Send(
                new CancelVoucherCommand(document, Read<CancelVoucherRequest>(held.Payload).Reason),
                cancellationToken),
        };
    }
}

/// <summary>A note left with a decision.</summary>
/// <param name="Note">The note; why, for a refusal.</param>
public sealed record EditDecisionRequest(string? Note = null);
