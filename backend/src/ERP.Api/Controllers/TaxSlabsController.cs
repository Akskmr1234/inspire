using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Inventory.Masters;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The tax rates a product can carry.</summary>
/// <remarks>Part of setting up what is sold, so behind the product permissions.</remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory/tax-rates")]
[Authorize]
[Produces("application/json")]
public sealed class TaxSlabsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="TaxSlabsController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public TaxSlabsController(ISender sender) => _sender = sender;

    /// <summary>Lists the firm's tax rates, lowest first.</summary>
    /// <param name="includeInactive">Whether withdrawn rates are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every rate, lowest percentage first.</returns>
    /// <response code="200">The list.</response>
    [HttpGet]
    [RequiresPermission("inventory", "product", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<TaxSlabSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<TaxSlabSummary>> result = await _sender.Send(
            new ListTaxSlabsQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a tax rate.</summary>
    /// <param name="request">The rate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new rate's identifier.</returns>
    /// <response code="201">Added.</response>
    /// <response code="400">The name or percentage is missing or out of range.</response>
    /// <response code="409">The code is taken.</response>
    [HttpPost]
    [RequiresPermission("inventory", "product", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveTaxSlabRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateTaxSlabCommand(
                request.Code, request.Name, request.Percentage, request.NameArabic, request.IsDefault),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a tax rate.</summary>
    /// <param name="id">The rate.</param>
    /// <param name="request">Its name, percentage and whether it is the default.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>A new percentage applies to documents raised from now on.</remarks>
    /// <response code="204">Saved.</response>
    /// <response code="404">No such rate in the selected firm.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveTaxSlabRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new UpdateTaxSlabCommand(
                id, request.Name, request.Percentage, request.NameArabic, request.IsDefault),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a tax rate from use, or returns it.</summary>
    /// <param name="id">The rate.</param>
    /// <param name="request">Whether it should be usable.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Saved.</response>
    /// <response code="404">No such rate in the selected firm.</response>
    [HttpPost("{id:guid}/active")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] SetTaxSlabActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetTaxSlabActiveCommand(id, request.IsActive), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>A tax rate being added or corrected.</summary>
/// <param name="Name">Its name, for example "VAT 5%".</param>
/// <param name="Percentage">The percentage.</param>
/// <param name="Code">The code when adding; made from the name when left out. Ignored when correcting.</param>
/// <param name="NameArabic">The name in Arabic.</param>
/// <param name="IsDefault">Whether new products start with it.</param>
public sealed record SaveTaxSlabRequest(
    [property: JsonRequired] string Name,
    [property: JsonRequired] decimal Percentage,
    string? Code = null,
    string? NameArabic = null,
    bool IsDefault = false);

/// <summary>Withdrawing or returning a tax rate.</summary>
/// <param name="IsActive">Whether it should be usable.</param>
public sealed record SetTaxSlabActiveRequest([property: JsonRequired] bool IsActive);
