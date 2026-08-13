using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Purchase;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The supplier master of section 13.</summary>
/// <remarks>
/// <para>
/// A supplier is a sub-ledger, as a customer is: a purchase is billed by one, a payment
/// settles against one, and the creditors report sums them. These endpoints create and
/// maintain ledgers of kind <c>Supplier</c> rather than a parallel record that would have
/// to be kept in step.
/// </para>
/// <para>
/// Nothing here deletes. A supplier with history is what every past purchase and the
/// creditors report point at; withdrawing one stops new documents naming them and leaves
/// the trail intact.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/purchase/suppliers")]
[Authorize]
[Produces("application/json")]
public sealed class SuppliersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="SuppliersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public SuppliersController(ISender sender) => _sender = sender;

    /// <summary>Lists suppliers, or finds one by name, code or number.</summary>
    /// <param name="search">Matched against code, name and mobile number.</param>
    /// <param name="activeOnly">Whether to exclude withdrawn suppliers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The suppliers, by name.</returns>
    /// <response code="200">The suppliers.</response>
    [HttpGet]
    [RequiresPermission("purchase", "supplier", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? search,
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SupplierResponse>> result = await _sender.Send(
            new GetSuppliersQuery(search, activeOnly), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one supplier.</summary>
    /// <param name="id">The supplier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The supplier, with their terms and contact details.</returns>
    /// <response code="200">The supplier as the system holds them.</response>
    /// <response code="404">No such supplier in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("purchase", "supplier", "view")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<SupplierResponse> result = await _sender.Send(
            new GetSupplierQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Creates a supplier.</summary>
    /// <param name="request">Their code, name, terms and contact details.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The supplier as created.</returns>
    /// <response code="201">The supplier.</response>
    /// <response code="400">The details were refused, and the reason says why.</response>
    /// <response code="409">The code is already used by another account.</response>
    [HttpPost]
    [RequiresPermission("purchase", "supplier", "create")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        Result<SupplierResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Changes a supplier's details.</summary>
    /// <param name="id">The supplier.</param>
    /// <param name="request">The details to change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The supplier as it now stands.</returns>
    /// <response code="200">The changed supplier.</response>
    /// <response code="404">No such supplier in the selected firm.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("purchase", "supplier", "edit")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<SupplierResponse> result = await _sender.Send(
            request with { SupplierId = id }, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Withdraws a supplier from use, or puts them back.</summary>
    /// <param name="id">The supplier.</param>
    /// <param name="request">Whether they may be bought from.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The supplier as it now stands.</returns>
    /// <response code="200">The changed supplier.</response>
    /// <response code="404">No such supplier in the selected firm.</response>
    [HttpPut("{id:guid}/active")]
    [RequiresPermission("purchase", "supplier", "edit")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] SetSupplierActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<SupplierResponse> result = await _sender.Send(
            new SetSupplierActiveCommand(id, request.IsActive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>Whether a supplier may be bought from.</summary>
/// <param name="IsActive">True to restore them, false to withdraw them.</param>
/// <remarks>
/// Required rather than defaulted, because the default of a boolean is <c>false</c> and a
/// caller who left it out would withdraw the supplier they meant to restore.
/// </remarks>
public sealed record SetSupplierActiveRequest([property: JsonRequired] bool IsActive);
