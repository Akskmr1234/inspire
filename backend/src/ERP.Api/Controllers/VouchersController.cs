using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Accounting.Reports;
using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.Domain.Platform;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Accounting vouchers: receipts, payments, journals, and contras.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/vouchers")]
[Authorize]
[Produces("application/json")]
public sealed class VouchersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="VouchersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public VouchersController(ISender sender) => _sender = sender;

    /// <summary>Creates a voucher and, by default, posts it to the ledgers.</summary>
    /// <param name="request">The voucher to create.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created voucher's identifier and issued number.</returns>
    /// <response code="201">Created, and posted unless a draft was requested.</response>
    /// <response code="400">The request was malformed.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">No firm or branch is selected, or permission is lacking.</response>
    /// <response code="404">A referenced ledger or the selected firm does not exist.</response>
    /// <response code="422">
    /// A business rule was violated - most often that debits do not equal credits,
    /// or that the period is closed.
    /// </response>
    [HttpPost]
    [RequiresPermission("accounting", "voucher", "create")]
    [ProducesResponseType(typeof(CreateVoucherResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateVoucherCommand request,
        CancellationToken cancellationToken)
    {
        Result<CreateVoucherResponse> result = await _sender.Send(request, cancellationToken);

        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // The Location header is built from the request path rather than with
        // CreatedAtAction. Route-based generation is fragile here for two reasons
        // that both bite silently: ASP.NET Core strips the "Async" suffix from
        // action names, so nameof(GetByIdAsync) does not name the action, and the
        // versioned route template needs a "version" value that is easy to omit.
        // Either mistake throws "No route matches the supplied values" *after* the
        // voucher has already been committed - a 500 for a request that in fact
        // succeeded, which is the worst of both outcomes.
        string location = $"{Request.Path.Value?.TrimEnd('/')}/{result.Value.VoucherId}";

        return Created(location, result.Value);
    }

    /// <summary>Lists a party's open bills, for a receipt or payment to settle bill by bill.</summary>
    /// <param name="ledgerId">The party.</param>
    /// <param name="type">Receivable for what a customer owes, payable for what is owed to a supplier.</param>
    /// <param name="asAt">The date the position is stated as at; today when omitted.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The party's open bills, oldest due first; empty when it has none.</returns>
    /// <remarks>
    /// The debtors and creditors reports narrowed to one party, behind the permission to
    /// view vouchers rather than reports: the clerk entering a receipt has to see what the
    /// customer owes without being given the whole debtors report.
    /// </remarks>
    [HttpGet("open-bills")]
    [RequiresPermission("accounting", "voucher", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<OutstandingBill>), StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenBillsAsync(
        [FromQuery] Guid ledgerId,
        [FromQuery] BillType type,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<OutstandingBillsResponse> result = await _sender.Send(
            new GetOutstandingBillsQuery(type, asAt ?? DateOnly.FromDateTime(DateTime.UtcNow), ledgerId),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value.Parties.SelectMany(party => party.Bills).ToList())
            : Problem(result.Error);
    }

    /// <summary>Fetches a voucher and its lines.</summary>
    /// <param name="id">The voucher identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The voucher.</returns>
    /// <response code="200">Found.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="404">No such voucher in the current tenant.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("accounting", "voucher", "view")]
    [ProducesResponseType(typeof(VoucherDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<VoucherDetailResponse> result =
            await _sender.Send(new GetVoucherByIdQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Corrects a voucher, keeping its number.</summary>
    /// <param name="id">The voucher.</param>
    /// <param name="request">The voucher as it should be, in full.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The voucher as it now stands.</returns>
    /// <response code="200">The corrected voucher.</response>
    /// <response code="400">The correction was refused, and the reason says why.</response>
    /// <response code="404">No such voucher in the selected firm.</response>
    /// <response code="422">
    /// The voucher is cancelled, was written by an invoice, or what it settled has moved on.
    /// </response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("accounting", "voucher", "edit")]
    [ProducesResponseType(typeof(CreateVoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateVoucherRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await HoldForApprovalAsync(
            _sender, EditDocumentKind.Voucher, id, EditAction.Edit, request, request.Reason, cancellationToken) is { } held)
        {
            return held;
        }

        Result<CreateVoucherResponse> result = await _sender.Send(EditCommand(id, request), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Cancels a voucher, undoing what it settled and recorded.</summary>
    /// <param name="id">The voucher.</param>
    /// <param name="request">Why it is being cancelled.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">The voucher was cancelled.</response>
    /// <response code="404">No such voucher in the selected firm.</response>
    /// <response code="422">The cancellation was refused, and the reason says why.</response>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("accounting", "voucher", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] CancelVoucherRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await HoldForApprovalAsync(
            _sender, EditDocumentKind.Voucher, id, EditAction.Cancel, request, request.Reason, cancellationToken) is { } held)
        {
            return held;
        }

        Result result = await _sender.Send(
            new CancelVoucherCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Builds the command that makes a correction, here or once it is approved.</summary>
    /// <param name="id">The document.</param>
    /// <param name="request">The correction as submitted.</param>
    /// <returns>The command.</returns>
    internal static UpdateVoucherCommand EditCommand(Guid id, UpdateVoucherRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CreateVoucherCommand changes = new(
            VoucherType.Journal,
            request.Date,
            request.Lines,
            request.CurrencyCode,
            request.ExchangeRate,
            request.ReferenceNumber,
            request.Narration,
            request.PaymentMode,
            request.PostImmediately);

        return new UpdateVoucherCommand(id, changes, request.Reason);
    }
}

/// <summary>A voucher as it should be, sent to correct one.</summary>
/// <param name="Date">The document date.</param>
/// <param name="Lines">Every line, as it should be.</param>
/// <param name="CurrencyCode">The entry currency. The firm's own when omitted.</param>
/// <param name="ExchangeRate">The rate to the base currency.</param>
/// <param name="ReferenceNumber">A related reference.</param>
/// <param name="Narration">The narration.</param>
/// <param name="PaymentMode">The payment mode, for a receipt or payment.</param>
/// <param name="PostImmediately">Post a draft once corrected. A posted voucher is always posted again.</param>
/// <param name="Reason">Why a posted voucher was changed.</param>
public sealed record UpdateVoucherRequest(
    [property: JsonRequired] DateOnly Date,
    IReadOnlyList<CreateVoucherLine> Lines,
    string? CurrencyCode = null,
    decimal ExchangeRate = 1m,
    string? ReferenceNumber = null,
    string? Narration = null,
    string? PaymentMode = null,
    bool PostImmediately = true,
    string? Reason = null);

/// <summary>Why a voucher is being cancelled.</summary>
/// <param name="Reason">Required, and kept on the voucher.</param>
public sealed record CancelVoucherRequest([property: JsonRequired] string Reason);
