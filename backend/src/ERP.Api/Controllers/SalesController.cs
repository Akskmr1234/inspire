using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Abstractions;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The sales invoices of section 12.</summary>
/// <remarks>
/// <para>
/// Entering and posting are separate endpoints, because they are separate events. A draft
/// has moved nothing and can be corrected; posting issues the stock, raises the debt and
/// writes the books in one transaction, and what it produced is reported back rather than
/// left to be looked up.
/// </para>
/// <para>
/// Nothing here deletes. A posted invoice is cancelled, which puts the goods back on the
/// shelf, withdraws the debt and takes both journals out of the balances while leaving
/// every document in place; a draft that was never posted is simply never posted.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sales/invoices")]
[Authorize]
[Produces("application/json")]
public sealed class SalesController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="SalesController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public SalesController(ISender sender) => _sender = sender;

    /// <summary>Lists sales documents, newest first.</summary>
    /// <param name="from">The earliest document date. Omit for no lower bound.</param>
    /// <param name="to">The latest. Omit for no upper bound.</param>
    /// <param name="kind">Invoices or returns. Omit for both.</param>
    /// <param name="status">One lifecycle state. Omit for all.</param>
    /// <param name="customerLedgerId">One customer. Omit for all.</param>
    /// <param name="search">Matched against the number and the customer's reference.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds, up to 200.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page, and how many rows the filter matched.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">The paging or the date range was refused.</response>
    /// <remarks>
    /// Paged, unlike the older lists in this API. A chart of accounts is a few hundred
    /// rows and a stock document list is bounded by its dates, but a busy shop raises
    /// thousands of invoices a month - so this is the shape the lists that follow will
    /// take. Returns appear among the invoices unless the kind narrows them out, because
    /// they are the same kind of document and a customer's history wants both.
    /// </remarks>
    [HttpGet]
    [RequiresPermission("sales", "invoice", "view")]
    [ProducesResponseType(
        typeof(PagedResult<SalesInvoiceSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] SalesDocumentKind? kind,
        [FromQuery] SalesInvoiceStatus? status,
        [FromQuery] Guid? customerLedgerId,
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<SalesInvoiceSummary>> result = await _sender.Send(
            new ListSalesInvoicesQuery(
                from,
                to,
                kind,
                status,
                customerLedgerId,
                search,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 50 : pageSize),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one invoice, with its lines and what posting it produced.</summary>
    /// <param name="id">The invoice.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The invoice in full.</returns>
    /// <response code="200">The invoice, its lines, its charges, and its postings.</response>
    /// <response code="404">No such invoice in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("sales", "invoice", "view")]
    [ProducesResponseType(typeof(SalesInvoiceDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<SalesInvoiceDetail> result = await _sender.Send(
            new GetSalesInvoiceQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters an invoice as a draft.</summary>
    /// <param name="request">What is being sold, and to whom.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft, numbered, with its totals.</returns>
    /// <response code="201">The draft.</response>
    /// <response code="400">The invoice was refused, and the reason says why.</response>
    /// <response code="403">No firm and branch are selected, or the permission is missing.</response>
    [HttpPost]
    [RequiresPermission("sales", "invoice", "create")]
    [ProducesResponseType(typeof(SalesInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSalesInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        Result<SalesInvoiceResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects an invoice, keeping its number.</summary>
    /// <param name="id">The invoice.</param>
    /// <param name="request">The invoice as it should be, in full.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The invoice as it now stands.</returns>
    /// <response code="200">The corrected invoice.</response>
    /// <response code="400">The correction was refused, and the reason says why.</response>
    /// <response code="404">No such invoice in the selected firm.</response>
    /// <response code="422">
    /// The invoice is cancelled, has been paid against, or could not be posted again.
    /// </response>
    /// <remarks>
    /// A draft is rewritten. A posted invoice has its issue, bill and journal cancelled -
    /// kept, as the record that it was posted once - and is rewritten and posted again in
    /// the same transaction, so nothing is half-undone if the new version is refused.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [RequiresPermission("sales", "invoice", "edit")]
    [ProducesResponseType(typeof(SalesInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSalesInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CreateSalesInvoiceCommand changes = new(
            request.Date,
            request.CustomerLedgerId,
            request.WarehouseId,
            request.Lines,
            request.Charges,
            request.Mode,
            request.ReferenceNumber,
            request.Narration,
            ReturnsInvoiceId: request.ReturnsInvoiceId);

        Result<SalesInvoiceResponse> result = await _sender.Send(
            new UpdateSalesInvoiceCommand(id, changes, request.Post, request.Reason),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Posts a draft: the goods leave, the debt is raised, the books move.</summary>
    /// <param name="id">The draft to post.</param>
    /// <param name="request">The terms, if they differ from the customer's own.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The issue, the bill and the journal it produced.</returns>
    /// <response code="200">What the posting produced.</response>
    /// <response code="404">No such invoice in the selected firm.</response>
    /// <response code="422">The sale was refused - short stock, a missing account - and the reason says which.</response>
    /// <remarks>
    /// Its own verb rather than a status field on an update, because posting is not an
    /// edit: it is the event that moves four aggregates at once, and it either happens
    /// completely or not at all.
    /// </remarks>
    [HttpPost("{id:guid}/post")]
    [RequiresPermission("sales", "invoice", "create")]
    [ProducesResponseType(typeof(PostSalesInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromBody] PostSalesInvoiceRequest? request,
        CancellationToken cancellationToken)
    {
        Result<PostSalesInvoiceResponse> result = await _sender.Send(
            new PostSalesInvoiceCommand(id, request?.CreditDays), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Cancels a posted invoice, putting back everything posting it moved.</summary>
    /// <param name="id">The invoice.</param>
    /// <param name="request">Why it is being cancelled.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">The invoice was cancelled.</response>
    /// <response code="404">No such invoice in the selected firm.</response>
    /// <response code="422">The cancellation was refused, and the reason says why.</response>
    /// <remarks>
    /// For an invoice that should never have been raised. Goods a customer has actually
    /// taken away come back as a sales return instead, and an invoice the customer has
    /// already paid against is refused here by name: a receipt has to stay where it was
    /// made, so what that situation needs is a credit note.
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("sales", "invoice", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] CancelSalesInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new CancelSalesInvoiceCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>An invoice as it should be, sent to correct one.</summary>
/// <param name="Date">The invoice date.</param>
/// <param name="CustomerLedgerId">The customer billed.</param>
/// <param name="WarehouseId">The warehouse the goods leave.</param>
/// <param name="Lines">Every line, as it should be.</param>
/// <param name="Charges">Every charge, as it should be.</param>
/// <param name="Mode">The tax mode. Unchanged when omitted.</param>
/// <param name="ReferenceNumber">The customer's own reference.</param>
/// <param name="Narration">What is printed on the invoice.</param>
/// <param name="ReturnsInvoiceId">For a return, the invoice it is against.</param>
/// <param name="Post">Post a draft once it is corrected. A posted invoice is always posted again.</param>
/// <param name="Reason">Why a posted invoice was changed.</param>
public sealed record UpdateSalesInvoiceRequest(
    DateOnly Date,
    Guid CustomerLedgerId,
    Guid WarehouseId,
    IReadOnlyList<SalesInvoiceLineInput> Lines,
    IReadOnlyList<SalesInvoiceChargeInput>? Charges = null,
    TaxMode? Mode = null,
    string? ReferenceNumber = null,
    string? Narration = null,
    Guid? ReturnsInvoiceId = null,
    bool Post = false,
    string? Reason = null);

/// <summary>Why an invoice is being cancelled.</summary>
/// <param name="Reason">
/// Required, and kept on the invoice. A cancellation nobody explained is one somebody has
/// to reconstruct from a stock ledger later.
/// </param>
public sealed record CancelSalesInvoiceRequest([property: JsonRequired] string Reason);

/// <summary>What may be stated when posting, beyond the invoice itself.</summary>
/// <param name="CreditDays">
/// How long the customer has to pay. Omit to use the terms on their own ledger.
/// </param>
public sealed record PostSalesInvoiceRequest(int? CreditDays = null);
