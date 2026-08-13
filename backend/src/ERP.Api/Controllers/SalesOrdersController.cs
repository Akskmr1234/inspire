using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Abstractions;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The sales orders of §12.9's chain, and §12.2's <em>Create Invoice From</em>.</summary>
/// <remarks>
/// <para>
/// An order records what a customer asked for. It reserves nothing: stock moves when an
/// invoice posts, so what the order carries is the outstanding quantity per line rather
/// than a hold on a shelf.
/// </para>
/// <para>
/// Entering, confirming and converting are separate endpoints because they are separate
/// events. A draft is a conversation and can be corrected; a confirmed order is something
/// the firm has agreed to; converting produces a <b>draft invoice</b>, which is then posted
/// through the ordinary sales endpoints - so nothing moves until somebody says so.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sales/orders")]
[Authorize]
[Produces("application/json")]
public sealed class SalesOrdersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="SalesOrdersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public SalesOrdersController(ISender sender) => _sender = sender;

    /// <summary>Lists orders, newest first.</summary>
    /// <param name="from">The earliest order date. Omit for no lower bound.</param>
    /// <param name="to">The latest. Omit for no upper bound.</param>
    /// <param name="status">One lifecycle state. Omit for all.</param>
    /// <param name="customerLedgerId">One customer. Omit for all.</param>
    /// <param name="search">Matched against the number and the customer's reference.</param>
    /// <param name="outstandingOnly">Only orders with goods still owed.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds, up to 200.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page, and how many rows the filter matched.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The paging or the date range was refused.</response>
    [HttpGet]
    [RequiresPermission("sales", "order", "view")]
    [ProducesResponseType(typeof(PagedResult<SalesOrderSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] SalesOrderStatus? status,
        [FromQuery] Guid? customerLedgerId,
        [FromQuery] string? search,
        [FromQuery] bool outstandingOnly,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<SalesOrderSummary>> result = await _sender.Send(
            new ListSalesOrdersQuery(
                from,
                to,
                status,
                customerLedgerId,
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
    [RequiresPermission("sales", "order", "view")]
    [ProducesResponseType(typeof(SalesOrderDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<SalesOrderDetail> result = await _sender.Send(
            new GetSalesOrderQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters an order as a draft.</summary>
    /// <param name="request">What was ordered, and by whom.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft, numbered, with its totals.</returns>
    /// <response code="201">The draft.</response>
    /// <response code="400">The order was refused, and the reason says why.</response>
    /// <response code="403">No firm and branch are selected, or the permission is missing.</response>
    [HttpPost]
    [RequiresPermission("sales", "order", "create")]
    [ProducesResponseType(typeof(SalesOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSalesOrderCommand request,
        CancellationToken cancellationToken)
    {
        Result<SalesOrderResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Confirms a draft, so invoices may be raised from it.</summary>
    /// <param name="id">The order.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The order as it now stands.</returns>
    /// <response code="200">The confirmed order.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="422">The order was refused - nothing on it, or already confirmed.</response>
    [HttpPost("{id:guid}/confirm")]
    [RequiresPermission("sales", "order", "create")]
    [ProducesResponseType(typeof(SalesOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<SalesOrderResponse> result = await _sender.Send(
            new ConfirmSalesOrderCommand(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Raises a draft invoice from a confirmed order.</summary>
    /// <param name="id">The order.</param>
    /// <param name="request">Which lines are going out, and from where.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The invoice, as a draft.</returns>
    /// <response code="201">The draft invoice.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="422">The conversion was refused, and the reason says why.</response>
    /// <remarks>
    /// Naming no lines converts everything still outstanding, which is the common path.
    /// The invoice comes back as a draft: posting it is the ordinary
    /// <c>POST /sales/invoices/{id}/post</c>, which is where the stock, the debt and the
    /// books actually move.
    /// </remarks>
    [HttpPost("{id:guid}/convert")]
    [RequiresPermission("sales", "invoice", "create")]
    [ProducesResponseType(typeof(SalesInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConvertAsync(
        Guid id,
        [FromBody] ConvertSalesOrderRequest? request,
        CancellationToken cancellationToken)
    {
        Result<SalesInvoiceResponse> result = await _sender.Send(
            new ConvertSalesOrderCommand(
                id, request?.Date, request?.Lines, request?.WarehouseId),
            cancellationToken);

        return result.IsSuccess
            ? Created(string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Closes an order short, or cancels one nothing has gone out against.</summary>
    /// <param name="id">The order.</param>
    /// <param name="request">Why it is being closed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">The order was closed.</response>
    /// <response code="404">No such order in the selected firm.</response>
    /// <response code="422">The order is already finished.</response>
    /// <remarks>
    /// One verb for both, because the difference is a fact about the order rather than a
    /// choice: one nothing has shipped against is cancelled, and a part-filled one is
    /// closed short. Both keep the reason, which is what an outstanding-orders report
    /// needs to explain why a line stopped being owed.
    /// </remarks>
    [HttpPost("{id:guid}/close")]
    [RequiresPermission("sales", "order", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseAsync(
        Guid id,
        [FromBody] CloseSalesOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new CloseSalesOrderCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>What may be stated when converting an order.</summary>
/// <param name="Date">The invoice date. Defaults to today.</param>
/// <param name="Lines">Which lines are going out. Omit for everything outstanding.</param>
/// <param name="WarehouseId">Where the goods actually ship from, if not the order's.</param>
public sealed record ConvertSalesOrderRequest(
    DateOnly? Date = null,
    IReadOnlyList<SalesOrderConversionLine>? Lines = null,
    Guid? WarehouseId = null);

/// <summary>Why an order is being closed.</summary>
/// <param name="Reason">
/// Required, and kept on the order. An order that stopped being owed for a reason nobody
/// recorded is one somebody has to reconstruct from an invoice trail.
/// </param>
public sealed record CloseSalesOrderRequest([property: JsonRequired] string Reason);
