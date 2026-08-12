using Asp.Versioning;
using ERP.Application.Sales;
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
/// Nothing here deletes. A posted invoice is cancelled - which is not yet built, because
/// putting back the goods, the debt and the journal is its own piece of work - and a draft
/// that was never posted is simply never posted.
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
}

/// <summary>What may be stated when posting, beyond the invoice itself.</summary>
/// <param name="CreditDays">
/// How long the customer has to pay. Omit to use the terms on their own ledger.
/// </param>
public sealed record PostSalesInvoiceRequest(int? CreditDays = null);
