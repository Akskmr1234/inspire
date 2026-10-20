using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Transfers between branches: requested, approved, dispatched and received.</summary>
/// <remarks>
/// Each step has its own permission so a firm can give them to different people: the
/// branch that wants the goods raises the request, a manager at the branch that holds
/// them approves it, its storekeeper dispatches, and the other branch receives.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory/stock-transfers")]
[Authorize]
[Produces("application/json")]
public sealed class StockTransfersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="StockTransfersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public StockTransfersController(ISender sender) => _sender = sender;

    /// <summary>Lists transfer requests over a date range.</summary>
    /// <param name="from">The earliest request date.</param>
    /// <param name="to">The latest request date.</param>
    /// <param name="status">One state. Omit for all.</param>
    /// <param name="warehouseId">Either end of the transfer. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The requests, newest first.</returns>
    [HttpGet]
    [RequiresPermission("inventory", "stock-transfer", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<StockTransferSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] StockTransferStatus? status,
        [FromQuery] Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<StockTransferSummary>> result = await _sender.Send(
            new ListStockTransferRequestsQuery(from, to, status, warehouseId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one request, with what the sending warehouse holds of each product.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request with its lines.</returns>
    [HttpGet("{id:guid}")]
    [RequiresPermission("inventory", "stock-transfer", "view")]
    [ProducesResponseType(typeof(StockTransferDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<StockTransferDetail> result = await _sender.Send(
            new GetStockTransferRequestQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Asks another warehouse for goods.</summary>
    /// <param name="request">What is wanted, and from where.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request as raised.</returns>
    [HttpPost]
    [RequiresPermission("inventory", "stock-transfer", "create")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] StockTransferRequestBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new CreateStockTransferRequestCommand(
                request.Date, request.FromWarehouseId, request.ToWarehouseId, Lines(request),
                request.Narration),
            cancellationToken);

        return result.IsSuccess
            ? Created(
                Url.Action(nameof(GetAsync), new { id = result.Value.StockTransferRequestId })
                    ?? string.Empty,
                result.Value)
            : Problem(result.Error);
    }

    /// <summary>Changes a request nobody has acted on yet.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">The request, whole.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request as it now stands.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("inventory", "stock-transfer", "create")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] StockTransferRequestBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new UpdateStockTransferRequestCommand(
                id, request.Date, request.FromWarehouseId, request.ToWarehouseId, Lines(request),
                request.Narration),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Agrees to send the goods, for as much of each as the source can spare.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">The quantities agreed where they differ from what was asked.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request, approved.</returns>
    [HttpPost("{id:guid}/approve")]
    [RequiresPermission("inventory", "stock-transfer", "approve")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody] ApproveStockTransferBody? request,
        CancellationToken cancellationToken)
    {
        Result<StockTransferResponse> result = await _sender.Send(
            new ApproveStockTransferCommand(
                id,
                request?.Lines?.Select(line => new StockTransferApprovalInput(line.ProductId, line.Quantity))
                    .ToList()),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Turns a request down.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request, rejected.</returns>
    [HttpPost("{id:guid}/reject")]
    [RequiresPermission("inventory", "stock-transfer", "approve")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        [FromBody] StockTransferReasonBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new RejectStockTransferCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Withdraws a request before anything has moved.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request, cancelled.</returns>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("inventory", "stock-transfer", "delete")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] StockTransferReasonBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new CancelStockTransferCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Sends the approved goods: out of the source and into goods in transit.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">The date, and the lines with the batches and serial numbers picked.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request, dispatched, and the transfer that moved the goods.</returns>
    [HttpPost("{id:guid}/dispatch")]
    [RequiresPermission("inventory", "stock-transfer", "edit")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DispatchAsync(
        Guid id,
        [FromBody] DispatchStockTransferBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new DispatchStockTransferCommand(
                id,
                request.Date,
                [
                    .. request.Lines.Select(line => new StockDocumentLineInput(
                        line.ProductId,
                        line.Quantity,
                        line.UnitId,
                        Remarks: line.Remarks,
                        BatchId: line.BatchId,
                        BatchNumber: line.BatchNumber,
                        SerialNumbers: line.SerialNumbers)),
                ],
                request.Narration),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Takes the dispatched goods into the destination.</summary>
    /// <param name="id">The request's identifier.</param>
    /// <param name="request">The date they arrived.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The request, received, and the transfer that moved the goods.</returns>
    [HttpPost("{id:guid}/receive")]
    [RequiresPermission("inventory", "stock-transfer", "edit")]
    [ProducesResponseType(typeof(StockTransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReceiveAsync(
        Guid id,
        [FromBody] ReceiveStockTransferBody request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<StockTransferResponse> result = await _sender.Send(
            new ReceiveStockTransferCommand(id, request.Date), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    private static List<StockTransferLineInput> Lines(StockTransferRequestBody request) =>
        [.. request.Lines.Select(line => new StockTransferLineInput(
            line.ProductId, line.Quantity, line.UnitId, line.Remarks))];
}

/// <summary>One product asked for.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">How much, in the unit given.</param>
/// <param name="UnitId">The unit, or null for the stock unit.</param>
/// <param name="Remarks">A remark.</param>
public sealed record StockTransferLineBody(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity,
    Guid? UnitId = null,
    string? Remarks = null);

/// <summary>A transfer request as entered.</summary>
/// <param name="Date">The date the goods are asked for.</param>
/// <param name="FromWarehouseId">The warehouse that holds them.</param>
/// <param name="ToWarehouseId">The warehouse that wants them.</param>
/// <param name="Lines">What is wanted.</param>
/// <param name="Narration">A note for the sending branch.</param>
public sealed record StockTransferRequestBody(
    [property: JsonRequired] DateOnly Date,
    [property: JsonRequired] Guid FromWarehouseId,
    [property: JsonRequired] Guid ToWarehouseId,
    [property: JsonRequired] IReadOnlyList<StockTransferLineBody> Lines,
    string? Narration = null);

/// <summary>The quantity agreed for one product, in its stock unit.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">The quantity agreed.</param>
public sealed record ApproveStockTransferLine(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity);

/// <summary>An approval, with the quantities agreed where they differ from what was asked.</summary>
/// <param name="Lines">The quantities agreed; omit to approve everything as asked.</param>
public sealed record ApproveStockTransferBody(IReadOnlyList<ApproveStockTransferLine>? Lines = null);

/// <summary>Why a request was rejected or cancelled.</summary>
/// <param name="Reason">The reason.</param>
public sealed record StockTransferReasonBody([property: JsonRequired] string Reason);

/// <summary>One line of a dispatch.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Quantity">How much went, in the unit given.</param>
/// <param name="UnitId">The unit, or null for the stock unit.</param>
/// <param name="Remarks">A remark.</param>
/// <param name="BatchId">The batch it was picked from.</param>
/// <param name="BatchNumber">The batch, by number, where its identifier is not to hand.</param>
/// <param name="SerialNumbers">The units sent.</param>
public sealed record DispatchStockTransferLine(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity,
    Guid? UnitId = null,
    string? Remarks = null,
    Guid? BatchId = null,
    string? BatchNumber = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>A dispatch as entered.</summary>
/// <param name="Date">The dispatch date.</param>
/// <param name="Lines">What went.</param>
/// <param name="Narration">A note for the dispatch.</param>
public sealed record DispatchStockTransferBody(
    [property: JsonRequired] DateOnly Date,
    [property: JsonRequired] IReadOnlyList<DispatchStockTransferLine> Lines,
    string? Narration = null);

/// <summary>A receipt.</summary>
/// <param name="Date">The date the goods arrived.</param>
public sealed record ReceiveStockTransferBody([property: JsonRequired] DateOnly Date);
