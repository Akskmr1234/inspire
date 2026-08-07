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

/// <summary>The stock operations of section 8.3.</summary>
/// <remarks>
/// One endpoint for every kind of document rather than one each. A receipt, an issue,
/// a transfer and an adjustment differ in what they mean and in which fields they
/// accept - which the type decides - and not at all in the shape of the request. Four
/// endpoints would be four copies of the same validation, and the fourth would drift.
/// <para>
/// Nothing here deletes. A posted document is cancelled, which reverses its movements
/// and leaves both the original and the reversal in the stock ledger, because a stock
/// ledger that can lose a movement is one nobody can reconcile against a count.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory/stock")]
[Authorize]
[Produces("application/json")]
public sealed class StockController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="StockController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public StockController(ISender sender) => _sender = sender;

    /// <summary>Lists stock documents over a date range.</summary>
    /// <param name="from">The earliest document date.</param>
    /// <param name="to">The latest document date.</param>
    /// <param name="type">One kind of operation. Omit for all.</param>
    /// <param name="warehouseId">One warehouse. Matches either end of a transfer.</param>
    /// <param name="status">One lifecycle state. Omit for all.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The documents, newest first.</returns>
    /// <response code="200">The documents.</response>
    [HttpGet("documents")]
    [RequiresPermission("inventory", "stock-adjustment", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<StockDocumentSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentsAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] StockDocumentType? type,
        [FromQuery] Guid? warehouseId,
        [FromQuery] StockDocumentStatus? status,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<StockDocumentSummary>> result = await _sender.Send(
            new ListStockDocumentsQuery(from, to, type, warehouseId, status),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one stock document, with what it said and what it did.</summary>
    /// <param name="id">The document.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The document, its lines, and the movements it produced.</returns>
    /// <response code="200">The document, its lines, and its movements.</response>
    /// <response code="404">No such document in the selected firm.</response>
    [HttpGet("documents/{id:guid}")]
    [RequiresPermission("inventory", "stock-adjustment", "view")]
    [ProducesResponseType(typeof(StockDocumentDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<StockDocumentDetail> result = await _sender.Send(
            new GetStockDocumentQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters a stock document and, by default, posts it.</summary>
    /// <param name="request">The document.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The document, its number, and what it moved.</returns>
    /// <remarks>
    /// The rate is only accepted on the documents that bring goods in from outside the
    /// firm's existing stock: an opening balance, a material receipt, and an adjustment
    /// upwards. Everything else is valued at what the position it leaves already says
    /// the goods cost, which is what average costing means.
    /// <para>
    /// A physical verification is entered as what was counted, not as what moved. The
    /// difference against the system's figure is what posts.
    /// </para>
    /// </remarks>
    /// <response code="201">Entered.</response>
    /// <response code="400">A unit does not convert, or a rate was given where none applies.</response>
    /// <response code="404">A product, unit, or warehouse is not in this firm.</response>
    /// <response code="422">There is not enough stock, or the year is closed.</response>
    [HttpPost("documents")]
    [RequiresPermission("inventory", "stock-adjustment", "create")]
    [ProducesResponseType(typeof(CreateStockDocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateDocumentAsync(
        [FromBody] CreateStockDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<CreateStockDocumentResponse> result = await _sender.Send(
            new CreateStockDocumentCommand(
                request.Type,
                request.Date,
                request.WarehouseId,
                [
                    .. request.Lines.Select(line => new StockDocumentLineInput(
                        line.ProductId, line.Quantity, line.UnitId, line.Rate, line.Remarks)),
                ],
                request.DestinationWarehouseId,
                request.ReferenceNumber,
                request.Narration,
                request.PostImmediately),
            cancellationToken);

        return result.IsSuccess
            ? Created(
                Url.Action(nameof(GetDocumentAsync), new { id = result.Value.StockDocumentId })
                    ?? string.Empty,
                result.Value)
            : Problem(result.Error);
    }

    /// <summary>Posts a draft, moving the stock it names.</summary>
    /// <param name="id">The document.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What it moved.</returns>
    /// <response code="200">Posted.</response>
    /// <response code="404">No such document in the selected firm.</response>
    /// <response code="422">Already posted, or there is not enough stock.</response>
    [HttpPost("documents/{id:guid}/post")]
    [RequiresPermission("inventory", "stock-adjustment", "approve")]
    [ProducesResponseType(typeof(CreateStockDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PostDocumentAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<CreateStockDocumentResponse> result = await _sender.Send(
            new PostStockDocumentCommand(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Cancels a posted document, reversing what it moved.</summary>
    /// <param name="id">The document.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Reversed at the cost each movement was valued at rather than at today's
    /// average, so cancelling a receipt removes exactly the value it added. A receipt
    /// whose goods have since been sold cannot be reversed at all: un-receiving goods
    /// that have left is not something the books can express, and the refusal says so.
    /// </remarks>
    /// <response code="204">Cancelled and reversed.</response>
    /// <response code="404">No such document in the selected firm.</response>
    /// <response code="422">Not posted, or the goods it brought in have gone.</response>
    [HttpPost("documents/{id:guid}/cancel")]
    [RequiresPermission("inventory", "stock-adjustment", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelDocumentAsync(
        Guid id,
        [FromBody] CancelStockDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new CancelStockDocumentCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Reads what is on hand and what it is worth.</summary>
    /// <param name="warehouseId">One warehouse. Omit for every one.</param>
    /// <param name="categoryId">One category. Omit for every one.</param>
    /// <param name="includeZero">Whether to include emptied positions.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The valuation, at weighted average cost.</returns>
    /// <response code="200">The valuation.</response>
    [HttpGet("valuation")]
    [RequiresPermission("inventory", "report", "view")]
    [ProducesResponseType(typeof(StockValuationReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetValuationAsync(
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool includeZero,
        CancellationToken cancellationToken)
    {
        Result<StockValuationReport> result = await _sender.Send(
            new StockValuationQuery(warehouseId, categoryId, includeZero), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one product's movements, with the position each left behind.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="warehouseId">One warehouse. Omit for every one.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stock ledger.</returns>
    /// <remarks>
    /// One product at a time. The running balance column only means anything within
    /// one product, and a ledger that mixed several would show a column of figures
    /// that appear to jump about.
    /// </remarks>
    /// <response code="200">The ledger.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpGet("ledger")]
    [RequiresPermission("inventory", "report", "view")]
    [ProducesResponseType(typeof(StockLedgerReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLedgerAsync(
        [FromQuery] Guid productId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        Result<StockLedgerReport> result = await _sender.Send(
            new StockLedgerQuery(productId, from, to, warehouseId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads what moved over a period, and how much of it.</summary>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="warehouseId">One warehouse. Omit for every one.</param>
    /// <param name="categoryId">One category. Omit for every one.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The movement of each product that moved.</returns>
    /// <response code="200">The movement.</response>
    [HttpGet("movement")]
    [RequiresPermission("inventory", "report", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<ItemMovementRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovementAsync(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ItemMovementRow>> result = await _sender.Send(
            new ItemMovementQuery(from, to, warehouseId, categoryId), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>One line of a stock document being entered.</summary>
/// <param name="ProductId">The product moving.</param>
/// <param name="Quantity">
/// How much, in <paramref name="UnitId"/>. Negative only on an adjustment; on a
/// physical verification this is what was counted rather than what moved.
/// </param>
/// <param name="UnitId">The unit it is entered in. Omit for the product's stock unit.</param>
/// <param name="Rate">What one stock unit cost, where the document carries a cost.</param>
/// <param name="Remarks">A line-level remark.</param>
public sealed record CreateStockDocumentLine(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity,
    Guid? UnitId = null,
    decimal Rate = 0m,
    string? Remarks = null);

/// <summary>Entering a stock document.</summary>
/// <param name="Type">The kind of operation.</param>
/// <param name="Date">The document date.</param>
/// <param name="WarehouseId">The warehouse acted on, or moved out of.</param>
/// <param name="Lines">What moved.</param>
/// <param name="DestinationWarehouseId">The warehouse a transfer moves into.</param>
/// <param name="ReferenceNumber">A related reference.</param>
/// <param name="Narration">The document narration.</param>
/// <param name="PostImmediately">Whether to post on save, or leave an editable draft.</param>
public sealed record CreateStockDocumentRequest(
    [property: JsonRequired] StockDocumentType Type,
    [property: JsonRequired] DateOnly Date,
    [property: JsonRequired] Guid WarehouseId,
    [property: JsonRequired] IReadOnlyList<CreateStockDocumentLine> Lines,
    Guid? DestinationWarehouseId = null,
    string? ReferenceNumber = null,
    string? Narration = null,
    bool PostImmediately = true);

/// <summary>Cancelling a stock document.</summary>
/// <param name="Reason">Why. Required, and kept on the document.</param>
public sealed record CancelStockDocumentRequest([property: JsonRequired] string Reason);
