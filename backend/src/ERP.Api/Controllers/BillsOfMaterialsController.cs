using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Inventory.Production;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Bills of materials: what goes into each finished good.</summary>
/// <remarks>
/// Kept with the product master and under its permissions, because a bill is part of
/// what a product is - the recipe for it - rather than something that happened to it.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory/boms")]
[Authorize]
[Produces("application/json")]
public sealed class BillsOfMaterialsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="BillsOfMaterialsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public BillsOfMaterialsController(ISender sender) => _sender = sender;

    /// <summary>Lists the firm's bills of materials.</summary>
    /// <param name="includeInactive">Whether withdrawn bills are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The bills.</returns>
    [HttpGet]
    [RequiresPermission("inventory", "product", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<BillOfMaterialsSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<BillOfMaterialsSummary>> result = await _sender.Send(
            new ListBillsOfMaterialsQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one bill with its components and what they cost today.</summary>
    /// <param name="id">The bill's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The bill with its components.</returns>
    [HttpGet("{id:guid}")]
    [RequiresPermission("inventory", "product", "view")]
    [ProducesResponseType(typeof(BillOfMaterialsDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<BillOfMaterialsDetail> result = await _sender.Send(
            new GetBillOfMaterialsQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Creates a bill for a finished good.</summary>
    /// <param name="request">The finished good, what it makes, and its components.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new bill's identifier.</returns>
    [HttpPost]
    [RequiresPermission("inventory", "product", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] BillOfMaterialsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateBillOfMaterialsCommand(
                request.ProductId, request.OutputQuantity, Components(request), request.Narration),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync), new { id = result.Value }) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Replaces a bill's output quantity, note and components.</summary>
    /// <param name="id">The bill's identifier.</param>
    /// <param name="request">The bill, whole. Its finished good does not change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] BillOfMaterialsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new UpdateBillOfMaterialsCommand(id, request.OutputQuantity, Components(request), request.Narration),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a bill from use, or brings it back.</summary>
    /// <param name="id">The bill's identifier.</param>
    /// <param name="request">Whether production may use it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/active")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] BillOfMaterialsActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetBillOfMaterialsActiveCommand(id, request.IsActive), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    private static List<BillOfMaterialsComponentInput> Components(BillOfMaterialsRequest request) =>
        [.. request.Components.Select(line => new BillOfMaterialsComponentInput(
            line.ProductId, line.Quantity, line.Remarks))];
}

/// <summary>One component of a bill.</summary>
/// <param name="ProductId">The material.</param>
/// <param name="Quantity">How much of it, in its stock unit.</param>
/// <param name="Remarks">A remark.</param>
public sealed record BillOfMaterialsComponentRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal Quantity,
    string? Remarks = null);

/// <summary>A bill of materials, as entered.</summary>
/// <param name="ProductId">The finished good.</param>
/// <param name="OutputQuantity">How much of it the components make, in its stock unit.</param>
/// <param name="Components">What goes into it.</param>
/// <param name="Narration">A note on how it is made.</param>
public sealed record BillOfMaterialsRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] decimal OutputQuantity,
    [property: JsonRequired] IReadOnlyList<BillOfMaterialsComponentRequest> Components,
    string? Narration = null);

/// <summary>Whether a bill may be used.</summary>
/// <param name="IsActive">True to bring it back, false to withdraw it.</param>
public sealed record BillOfMaterialsActiveRequest([property: JsonRequired] bool IsActive);
