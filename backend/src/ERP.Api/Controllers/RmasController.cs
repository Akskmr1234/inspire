using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Service;
using ERP.Domain.Service;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>
/// Return merchandise authorisations: goods coming back from customers or going back to
/// suppliers, from the request through approval and inspection to the exchange or refund.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/service/rmas")]
[Authorize]
[Produces("application/json")]
public sealed class RmasController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="RmasController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public RmasController(ISender sender) => _sender = sender;

    /// <summary>Lists returns, newest first.</summary>
    /// <param name="direction">Only those going this way.</param>
    /// <param name="status">Only those standing here.</param>
    /// <param name="search">Text in the number, party, invoice, reason or a serial number.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The returns.</returns>
    [HttpGet]
    [RequiresPermission("service", "rma", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<RmaSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] RmaDirection? direction,
        [FromQuery] RmaStatus? status,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RmaSummary>> result = await _sender.Send(
            new ListRmasQuery(direction, status, search, from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one return with its products, photographs and history.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The return.</returns>
    [HttpGet("{id:guid}")]
    [RequiresPermission("service", "rma", "view")]
    [ProducesResponseType(typeof(RmaDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<RmaDetail> result = await _sender.Send(new GetRmaQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one photograph.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="photoId">The photograph's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The picture, as a data URL.</returns>
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    [RequiresPermission("service", "rma", "view")]
    [ProducesResponseType(typeof(RmaPhotoContent), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        Result<string> result = await _sender.Send(new GetRmaPhotoQuery(id, photoId), cancellationToken);

        return result.IsSuccess ? Ok(new RmaPhotoContent(result.Value)) : Problem(result.Error);
    }

    /// <summary>Asks for a return.</summary>
    /// <param name="request">The return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new return's identifier.</returns>
    [HttpPost]
    [RequiresPermission("service", "rma", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] RmaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Date is not { } date || request.Direction is not { } direction
            || request.PartyLedgerId is not { } party || request.WarehouseId is not { } warehouse)
        {
            return Problem(Error.Validation(
                "Rma.HeaderRequired", "Give the date, the direction, the customer or supplier, and the godown."));
        }

        Result<Guid> result = await _sender.Send(
            new CreateRmaCommand(date, direction, party, warehouse, request.ToInput()), cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync), new { id = result.Value }) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Replaces what a return awaiting approval says.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">The return, whole. Its date, direction, party and godown do not change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] RmaRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(new UpdateRmaCommand(id, request.ToInput()), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Approves a return.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">A note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/approve")]
    [RequiresPermission("service", "rma", "approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Approve, request, cancellationToken);

    /// <summary>Refuses a return.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/reject")]
    [RequiresPermission("service", "rma", "approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> RejectAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Reject, request, cancellationToken);

    /// <summary>Withdraws a return.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("service", "rma", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Cancel, request, cancellationToken);

    /// <summary>Records that the goods changed hands.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">A note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/receive")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> ReceiveAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Receive, request, cancellationToken);

    /// <summary>Records that the goods were repaired and handed back.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">What was done.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/repair")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> RepairAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Repair, request, cancellationToken);

    /// <summary>Closes a return.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">A note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/close")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> CloseAsync(
        Guid id,
        [FromBody] RmaNoteRequest? request,
        CancellationToken cancellationToken) =>
        StepAsync(id, RmaStep.Close, request, cancellationToken);

    /// <summary>Records what the inspection found.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">The condition, the notes and the day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/inspect")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> InspectAsync(
        Guid id,
        [FromBody] RmaInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new InspectRmaCommand(id, request.Condition, request.Notes, request.On), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Keeps a photograph as evidence.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">The picture and what it shows.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The photograph's identifier.</returns>
    [HttpPost("{id:guid}/photos")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddPhotoAsync(
        Guid id,
        [FromBody] RmaPhotoRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new AddRmaPhotoCommand(id, request.Content, request.Caption), cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/service/rmas/{id}/photos/{result.Value}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Removes a photograph.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="photoId">The photograph's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("{id:guid}/photos/{photoId:guid}")]
    [RequiresPermission("service", "rma", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemovePhotoAsync(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(new RemoveRmaPhotoCommand(id, photoId), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Exchanges the goods.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">The godown of saleable stock, the day and a note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The transfer that moved the goods, if any moved.</returns>
    [HttpPost("{id:guid}/replace")]
    [RequiresPermission("service", "rma", "approve")]
    [ProducesResponseType(typeof(RmaOutcomeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplaceAsync(
        Guid id,
        [FromBody] RmaReplaceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<RmaOutcomeResponse> result = await _sender.Send(
            new ReplaceRmaCommand(id, request.StockWarehouseId, request.Date, request.Note), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Settles the return with a draft credit note or debit note.</summary>
    /// <param name="id">The return's identifier.</param>
    /// <param name="request">The note's date and a remark.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft note.</returns>
    [HttpPost("{id:guid}/refund")]
    [RequiresPermission("service", "rma", "approve")]
    [ProducesResponseType(typeof(RmaOutcomeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RefundAsync(
        Guid id,
        [FromBody] RmaRefundRequest? request,
        CancellationToken cancellationToken)
    {
        Result<RmaOutcomeResponse> result = await _sender.Send(
            new RefundRmaCommand(id, request?.Date, request?.Note), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    private async Task<IActionResult> StepAsync(
        Guid id,
        RmaStep step,
        RmaNoteRequest? request,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(new RmaStepCommand(id, step, request?.Note), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>A return, as entered.</summary>
/// <param name="Date">The day it was asked for. Read only when it is created.</param>
/// <param name="Direction">Which way the goods go back. Read only when it is created.</param>
/// <param name="PartyLedgerId">The customer or supplier. Read only when it is created.</param>
/// <param name="WarehouseId">The godown the returned goods are kept in. Read only when it is created.</param>
/// <param name="Reason">Why the goods are going back.</param>
/// <param name="Narration">A note.</param>
/// <param name="InvoiceReference">The invoice as the party quoted it.</param>
/// <param name="SalesInvoiceId">The sale, for a customer's return.</param>
/// <param name="PurchaseInvoiceId">The purchase, for a return to a supplier.</param>
/// <param name="Lines">The products going back.</param>
public sealed record RmaRequest(
    DateOnly? Date,
    RmaDirection? Direction,
    Guid? PartyLedgerId,
    Guid? WarehouseId,
    [property: JsonRequired] string Reason,
    string? Narration,
    string? InvoiceReference,
    Guid? SalesInvoiceId,
    Guid? PurchaseInvoiceId,
    [property: JsonRequired] IReadOnlyList<RmaLineInput> Lines)
{
    /// <summary>Converts the request to what the commands take.</summary>
    /// <returns>The return's content.</returns>
    public RmaInput ToInput() =>
        new(Reason, Narration, InvoiceReference, SalesInvoiceId, PurchaseInvoiceId, Lines);
}

/// <summary>A note left with a step.</summary>
/// <param name="Note">The note; the reason, for a refusal or a withdrawal.</param>
public sealed record RmaNoteRequest(string? Note = null);

/// <summary>What the inspection found.</summary>
/// <param name="Condition">The condition the goods are in.</param>
/// <param name="Notes">What was found.</param>
/// <param name="On">The day; today where none is given.</param>
public sealed record RmaInspectionRequest([property: JsonRequired] RmaCondition Condition, string? Notes, DateOnly? On);

/// <summary>A photograph.</summary>
/// <param name="Content">The picture, as a data URL.</param>
/// <param name="Caption">What it shows.</param>
public sealed record RmaPhotoRequest([property: JsonRequired] string Content, string? Caption);

/// <summary>A photograph's picture.</summary>
/// <param name="Content">The picture, as a data URL.</param>
public sealed record RmaPhotoContent(string Content);

/// <summary>How the goods are exchanged.</summary>
/// <param name="StockWarehouseId">The godown of saleable stock.</param>
/// <param name="Date">The day; today where none is given.</param>
/// <param name="Note">A note.</param>
public sealed record RmaReplaceRequest([property: JsonRequired] Guid StockWarehouseId, DateOnly? Date, string? Note);

/// <summary>How the return is refunded.</summary>
/// <param name="Date">The note's date; today where none is given.</param>
/// <param name="Note">A remark.</param>
public sealed record RmaRefundRequest(DateOnly? Date = null, string? Note = null);
