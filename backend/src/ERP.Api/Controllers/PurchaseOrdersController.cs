using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Abstractions;
using ERP.Application.Purchase;
using ERP.Domain.Purchase;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The purchase side of §12.9's chain, and of §12.2's <em>Create Invoice From</em>.</summary>
/// <remarks>
/// <para>
/// An order records what the firm asked a supplier for. It commits no money: nothing reaches
/// the nominal ledger and no bill is raised, so a supplier's balance stays what the firm has
/// been invoiced rather than what it has ordered. What the order carries is the outstanding
/// quantity per line, which is the buyer's chase list.
/// </para>
/// <para>
/// Entering, confirming and converting are separate endpoints because they are separate
/// events. A draft is a buyer working out what to ask for; a confirmed order is something
/// the firm has placed; converting produces a <b>draft purchase</b>, which is then posted
/// through the ordinary purchase endpoints - so the goods do not reach a shelf until
/// somebody says they have arrived.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/purchase/orders")]
[Authorize]
[Produces("application/json")]
public sealed class PurchaseOrdersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="PurchaseOrdersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public PurchaseOrdersController(ISender sender) => _sender = sender;

    /// <summary>Lists orders, newest first.</summary>
    /// <param name="from">The earliest order date. Omit for no lower bound.</param>
    /// <param name="to">The latest. Omit for no upper bound.</param>
    /// <param name="status">One lifecycle state. Omit for all.</param>
    /// <param name="supplierLedgerId">One supplier. Omit for all.</param>
    /// <param name="search">Matched against the number and the supplier's reference.</param>
    /// <param name="outstandingOnly">Only orders with goods still owed.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds, up to 200.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page, and how many rows the filter matched.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The paging or the date range was refused.</response>
    [HttpGet]
    [RequiresPermission("purchase", "order", "view")]
    [ProducesResponseType(typeof(PagedResult<PurchaseOrderSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] Guid? supplierLedgerId,
        [FromQuery] string? search,
        [FromQuery] bool outstandingOnly,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PurchaseOrderSummary>> result = await _sender.Send(
            new ListPurchaseOrdersQuery(
                from,
                to,
                status,
                supplierLedgerId,
                search,
                outstandingOnly,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 50 : pageSize),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one order, with what is still owed on each line.</summary>
    /// <param name="id">The order.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The order in full.</returns>
    /// <response code="200">The order, its lines and its charges.</response>
    /// <response code="404">No such order in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("purchase", "order", "view")]
    [ProducesResponseType(typeof(PurchaseOrderDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<PurchaseOrderDetail> result = await _sender.Send(
            new GetPurchaseOrderQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters an order as a draft.</summary>
    /// <param name="request">What was ordered, and from whom.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft, numbered, with its totals.</returns>
    /// <response code="201">The draft.</response>
    /// <response code="400">The order was refused, and the reason says why.</response>
    /// <response code="403">No firm and branch are selected, or the permission is missing.</response>
    [HttpPost]
    [RequiresPermission("purchase", "order", "create")]
    [ProducesResponseType(typeof(PurchaseOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreatePurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        Result<PurchaseOrderResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Confirms a draft, so purchases may be raised from it.</summary>
    /// <param name="id">The order.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The order as it now stands.</returns>
    /// <response code="200">The confirmed order.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="422">The order was refused - nothing on it, or already confirmed.</response>
    [HttpPost("{id:guid}/confirm")]
    [RequiresPermission("purchase", "order", "create")]
    [ProducesResponseType(typeof(PurchaseOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<PurchaseOrderResponse> result = await _sender.Send(
            new ConfirmPurchaseOrderCommand(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Raises a draft purchase from a confirmed order.</summary>
    /// <param name="id">The order.</param>
    /// <param name="request">Which lines have arrived, in what batches, and where.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The purchase, as a draft.</returns>
    /// <response code="201">The draft purchase.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="409">That supplier invoice number is already on file.</response>
    /// <response code="422">The conversion was refused, and the reason says why.</response>
    /// <remarks>
    /// Naming no lines converts everything still outstanding. The purchase comes back as a
    /// draft: posting it is the ordinary <c>POST /purchase/invoices/{id}/post</c>, which is
    /// where the goods, the debt and the books actually move.
    /// <para>
    /// Batch numbers are <b>typed</b> here rather than chosen from a list, which is where
    /// this stops mirroring the sales conversion: a purchase is usually the moment a batch
    /// comes into existence, so the number is read off the supplier's carton.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/convert")]
    [RequiresPermission("purchase", "invoice", "create")]
    [ProducesResponseType(typeof(PurchaseInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConvertAsync(
        Guid id,
        [FromBody] ConvertPurchaseOrderRequest? request,
        CancellationToken cancellationToken)
    {
        Result<PurchaseInvoiceResponse> result = await _sender.Send(
            new ConvertPurchaseOrderCommand(
                id,
                request?.Date,
                request?.Lines,
                request?.WarehouseId,
                request?.SupplierInvoiceNumber,
                request?.SupplierInvoiceDate),
            cancellationToken);

        return result.IsSuccess
            ? Created(string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Closes an order short, or cancels one nothing has arrived against.</summary>
    /// <param name="id">The order.</param>
    /// <param name="request">Why it is being closed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">The order was closed.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="422">The order is already finished.</response>
    /// <remarks>
    /// One verb for both, because the difference is a fact about the order rather than a
    /// choice: one nothing has arrived against is cancelled, and a part-filled one is closed
    /// short. Both keep the reason, which is what an outstanding-orders report needs to
    /// explain why a line stopped being owed.
    /// </remarks>
    [HttpPost("{id:guid}/close")]
    [RequiresPermission("purchase", "order", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseAsync(
        Guid id,
        [FromBody] ClosePurchaseOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new ClosePurchaseOrderCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>What may be stated when converting an order.</summary>
/// <param name="Date">The purchase date. Defaults to today.</param>
/// <param name="Lines">Which lines have arrived. Omit for everything outstanding.</param>
/// <param name="WarehouseId">Where the goods actually arrived, if not the order's.</param>
/// <param name="SupplierInvoiceNumber">The number printed on the supplier's own invoice.</param>
/// <param name="SupplierInvoiceDate">The date printed on it.</param>
public sealed record ConvertPurchaseOrderRequest(
    DateOnly? Date = null,
    IReadOnlyList<PurchaseOrderConversionLine>? Lines = null,
    Guid? WarehouseId = null,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null);

/// <summary>Why an order is being closed.</summary>
/// <param name="Reason">
/// Required, and kept on the order. An order that stopped being owed for a reason nobody
/// recorded is one somebody has to reconstruct from a purchase trail.
/// </param>
public sealed record ClosePurchaseOrderRequest([property: JsonRequired] string Reason);
