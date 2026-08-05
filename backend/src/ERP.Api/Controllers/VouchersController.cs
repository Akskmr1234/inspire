using Asp.Versioning;
using ERP.Application.Accounting.Vouchers;
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

    /// <summary>Fetches a voucher and its lines.</summary>
    /// <param name="id">The voucher identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The voucher.</returns>
    /// <response code="200">Found.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="404">No such voucher in the current tenant.</response>
    [HttpGet("{id:guid}")]
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
}
