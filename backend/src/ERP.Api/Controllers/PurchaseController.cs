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

/// <summary>The purchases of section 13.</summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="SalesController"/>. Entering and posting are separate
/// endpoints because they are separate events: a draft has moved nothing and can be
/// corrected while somebody keys it off the supplier's document; posting receives the
/// stock, raises the debt and writes the books in one transaction.
/// </para>
/// <para>
/// Nothing here deletes. A posted purchase is cancelled, which takes the goods back off
/// the shelf, withdraws the debt and takes both journals out of the balances while leaving
/// every document in place; a draft that was never posted is simply never posted.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/purchase/invoices")]
[Authorize]
[Produces("application/json")]
public sealed class PurchaseController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="PurchaseController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public PurchaseController(ISender sender) => _sender = sender;

    /// <summary>Lists purchase documents, newest first.</summary>
    /// <param name="from">The earliest document date. Omit for no lower bound.</param>
    /// <param name="to">The latest. Omit for no upper bound.</param>
    /// <param name="kind">Purchases or returns. Omit for both.</param>
    /// <param name="status">One lifecycle state. Omit for all.</param>
    /// <param name="supplierLedgerId">One supplier. Omit for all.</param>
    /// <param name="search">Matched against both numbers: the firm's and the supplier's.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds, up to 200.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page, and how many rows the filter matched.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The paging or the date range was refused.</response>
    [HttpGet]
    [RequiresPermission("purchase", "invoice", "view")]
    [ProducesResponseType(
        typeof(PagedResult<PurchaseInvoiceSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] PurchaseDocumentKind? kind,
        [FromQuery] PurchaseInvoiceStatus? status,
        [FromQuery] Guid? supplierLedgerId,
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PurchaseInvoiceSummary>> result = await _sender.Send(
            new ListPurchaseInvoicesQuery(
                from,
                to,
                kind,
                status,
                supplierLedgerId,
                search,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 50 : pageSize),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one purchase, with its lines and what posting it produced.</summary>
    /// <param name="id">The document.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The purchase in full.</returns>
    /// <response code="200">The purchase, its lines, its charges, and its postings.</response>
    /// <response code="404">No such purchase in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("purchase", "invoice", "view")]
    [ProducesResponseType(typeof(PurchaseInvoiceDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<PurchaseInvoiceDetail> result = await _sender.Send(
            new GetPurchaseInvoiceQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters a purchase as a draft.</summary>
    /// <param name="request">What is being bought, and from whom.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft, numbered, with its totals.</returns>
    /// <response code="201">The draft.</response>
    /// <response code="400">The purchase was refused, and the reason says why.</response>
    /// <response code="403">No firm and branch are selected, or the permission is missing.</response>
    [HttpPost]
    [RequiresPermission("purchase", "invoice", "create")]
    [ProducesResponseType(typeof(PurchaseInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreatePurchaseInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        Result<PurchaseInvoiceResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Posts a draft: the goods arrive, the debt is raised, the books move.</summary>
    /// <param name="id">The draft to post.</param>
    /// <param name="request">The terms, if they differ from the supplier's own.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The receipt, the bill and the journal it produced.</returns>
    /// <response code="200">What the posting produced.</response>
    /// <response code="404">No such purchase in the selected firm.</response>
    /// <response code="422">The purchase was refused - a missing account, a batch that does not exist - and the reason says which.</response>
    [HttpPost("{id:guid}/post")]
    [RequiresPermission("purchase", "invoice", "create")]
    [ProducesResponseType(typeof(PostPurchaseInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromBody] PostPurchaseInvoiceRequest? request,
        CancellationToken cancellationToken)
    {
        Result<PostPurchaseInvoiceResponse> result = await _sender.Send(
            new PostPurchaseInvoiceCommand(id, request?.CreditDays), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Cancels a posted purchase, putting back everything posting it moved.</summary>
    /// <param name="id">The document.</param>
    /// <param name="request">Why it is being cancelled.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">The purchase was cancelled.</response>
    /// <response code="404">No such purchase in the selected firm.</response>
    /// <response code="422">The cancellation was refused, and the reason says why.</response>
    /// <remarks>
    /// For a purchase that should never have been entered. Goods the firm has accepted and
    /// is sending back go on a purchase return instead, and a purchase the firm has
    /// already paid against is refused here by name: a payment has to stay where it was
    /// made, so what that situation needs is a debit note.
    /// <para>
    /// It can also be refused because the goods are gone. Taking a receipt back removes
    /// stock from a shelf, and a purchase whose goods have since been sold has nothing
    /// left to remove.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("purchase", "invoice", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] CancelPurchaseInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new CancelPurchaseInvoiceCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Why a purchase is being cancelled.</summary>
/// <param name="Reason">
/// Required, and kept on the document. A cancellation nobody explained is one somebody has
/// to reconstruct from a stock ledger later.
/// </param>
public sealed record CancelPurchaseInvoiceRequest([property: JsonRequired] string Reason);

/// <summary>What may be stated when posting, beyond the document itself.</summary>
/// <param name="CreditDays">
/// How long the firm has to pay. Omit to use the terms on the supplier's own ledger.
/// </param>
public sealed record PostPurchaseInvoiceRequest(int? CreditDays = null);
