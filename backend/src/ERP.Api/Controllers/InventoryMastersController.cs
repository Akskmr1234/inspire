using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Inventory.Masters;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>
/// The inventory masters: units of measurement, categories, brands, and warehouses.
/// </summary>
/// <remarks>
/// One controller for four masters, because they are maintained together and none is
/// large enough to be worth its own file. They are separately permissioned, though -
/// units and warehouses are configuration a stock controller sets up once, while
/// categories and brands are edited by whoever adds products.
/// <para>
/// None of the four is ever deleted. A record already named on a document must go on
/// meaning what it meant, so withdrawal is a flag rather than a removal.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory")]
[Authorize]
[Produces("application/json")]
public sealed class InventoryMastersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="InventoryMastersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public InventoryMastersController(ISender sender) => _sender = sender;

    // ------------------------------------------------------------------- units

    /// <summary>Lists the units of measurement.</summary>
    /// <param name="includeInactive">Whether to include units withdrawn from use.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The units, base units first, then by code.</returns>
    /// <remarks>
    /// A unit either is a base or converts to one. Units sharing a base form a
    /// measurement group, and only units of the same group may be substituted for one
    /// another on a document.
    /// </remarks>
    /// <response code="200">The units.</response>
    [HttpGet("units")]
    [RequiresPermission("inventory", "unit", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<UnitSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnitsAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<UnitSummary>> result = await _sender.Send(
            new ListUnitsQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a unit of measurement.</summary>
    /// <param name="request">The unit.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new unit's identifier.</returns>
    /// <remarks>
    /// Omit <c>baseUnitId</c> to start a new measurement group; supply it to add a unit
    /// converting to an existing base. A base cannot itself be derived: allowing a Box
    /// to be two Packs of twelve makes every conversion compound, and with a fractional
    /// factor that is where the rounding error comes from.
    /// </remarks>
    /// <response code="201">Created.</response>
    /// <response code="400">The unit is invalid, or the base is itself derived.</response>
    /// <response code="409">The code is already used by another unit.</response>
    [HttpPost("units")]
    [RequiresPermission("inventory", "unit", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUnitAsync(
        [FromBody] CreateUnitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateUnitCommand(
                request.Code, request.Name, request.BaseUnitId,
                request.ConversionFactor, request.Symbol, request.DecimalPlaces),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetUnitsAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Renames a unit of measurement.</summary>
    /// <param name="id">The unit.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// The code and the conversion factor are not editable. Both are relied on by
    /// documents already entered, and changing either would silently restate quantities
    /// recorded months ago.
    /// </remarks>
    /// <response code="204">Renamed.</response>
    /// <response code="404">No such unit in the selected firm.</response>
    [HttpPut("units/{id:guid}")]
    [RequiresPermission("inventory", "unit", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameUnitAsync(
        Guid id,
        [FromBody] RenameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new RenameUnitCommand(id, request.Name), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    // -------------------------------------------------------------- categories

    /// <summary>Lists the product categories and sub-classes.</summary>
    /// <param name="includeInactive">Whether to include withdrawn categories.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The categories, by code.</returns>
    /// <remarks>
    /// A sub-class is a category with a parent. The legacy ribbon names them
    /// separately; they are one thing here, which is what allows a level below
    /// sub-class the day a reporting hierarchy wants one.
    /// </remarks>
    /// <response code="200">The categories.</response>
    [HttpGet("categories")]
    [RequiresPermission("inventory", "category", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<CategorySummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategoriesAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CategorySummary>> result = await _sender.Send(
            new ListCategoriesQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a category, optionally beneath an existing one.</summary>
    /// <param name="request">The category.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new category's identifier.</returns>
    /// <response code="201">Created.</response>
    /// <response code="409">The code is already used by another category.</response>
    [HttpPost("categories")]
    [RequiresPermission("inventory", "category", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategoryAsync(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateCategoryCommand(
                request.Code, request.Name, request.ParentId, request.NameArabic),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetCategoriesAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    // ------------------------------------------------------------------ brands

    /// <summary>Lists the brands.</summary>
    /// <param name="includeInactive">Whether to include withdrawn brands.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The brands, by code.</returns>
    /// <response code="200">The brands.</response>
    [HttpGet("brands")]
    [RequiresPermission("inventory", "category", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<BrandSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBrandsAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<BrandSummary>> result = await _sender.Send(
            new ListBrandsQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a brand.</summary>
    /// <param name="request">The brand.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new brand's identifier.</returns>
    /// <response code="201">Created.</response>
    /// <response code="409">The code is already used by another brand.</response>
    [HttpPost("brands")]
    [RequiresPermission("inventory", "category", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBrandAsync(
        [FromBody] CreateBrandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateBrandCommand(request.Code, request.Name, request.NameArabic),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetBrandsAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    // -------------------------------------------------------------- warehouses

    /// <summary>Lists the warehouses.</summary>
    /// <param name="includeInactive">Whether to include withdrawn warehouses.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The warehouses, the default first, then by code.</returns>
    /// <response code="200">The warehouses.</response>
    [HttpGet("warehouses")]
    [RequiresPermission("inventory", "warehouse", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<WarehouseSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWarehousesAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WarehouseSummary>> result = await _sender.Send(
            new ListWarehousesQuery(includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a warehouse.</summary>
    /// <param name="request">The warehouse.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new warehouse's identifier.</returns>
    /// <remarks>
    /// The first warehouse a firm creates becomes its default, since a firm with stock
    /// locations and no default would have every document refuse to fill itself in.
    /// </remarks>
    /// <response code="201">Created.</response>
    /// <response code="409">The code is already used by another warehouse.</response>
    [HttpPost("warehouses")]
    [RequiresPermission("inventory", "warehouse", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateWarehouseAsync(
        [FromBody] CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateWarehouseCommand(
                request.Code, request.Name, request.BranchId,
                request.NameArabic, request.Address),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetWarehousesAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Makes one warehouse the one new documents default to.</summary>
    /// <param name="id">The warehouse.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// The previous default is demoted in the same transaction. A withdrawn warehouse
    /// cannot be promoted: the default is what a document fills itself in with, and
    /// offering one nobody may post to puts the error at the end of data entry rather
    /// than the start.
    /// </remarks>
    /// <response code="204">Promoted.</response>
    /// <response code="404">No such warehouse in the selected firm.</response>
    /// <response code="422">The warehouse has been withdrawn from use.</response>
    [HttpPost("warehouses/{id:guid}/default")]
    [RequiresPermission("inventory", "warehouse", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetDefaultWarehouseAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new SetDefaultWarehouseCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    // ------------------------------------------------------------- withdrawal

    /// <summary>Withdraws an inventory master from use, or returns it.</summary>
    /// <param name="kind">Which master.</param>
    /// <param name="id">The record.</param>
    /// <param name="request">Whether it should be usable.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// None of these is ever deleted. Documents already naming one must go on meaning
    /// what they meant, and a record that vanished would leave their quantities and
    /// classifications unreadable. The default warehouse cannot be withdrawn while it
    /// holds that role.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such record in the selected firm.</response>
    /// <response code="422">It is the default warehouse.</response>
    [HttpPost("{kind}/{id:guid}/active")]
    [RequiresPermission("inventory", "category", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetActiveAsync(
        InventoryMasterKind kind,
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetMasterActiveCommand(kind, id, request.IsActive), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Adding a unit of measurement.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="BaseUnitId">The base it converts to, or null to start a new group.</param>
/// <param name="ConversionFactor">How many base units one of this is worth.</param>
/// <param name="Symbol">The short form printed on documents.</param>
/// <param name="DecimalPlaces">How many decimals a quantity may carry.</param>
public sealed record CreateUnitRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    Guid? BaseUnitId = null,
    decimal ConversionFactor = 1m,
    string? Symbol = null,
    int DecimalPlaces = 0);

/// <summary>Adding a category.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The category name.</param>
/// <param name="ParentId">The category it sits beneath, or null for the top level.</param>
/// <param name="NameArabic">The name in Arabic.</param>
public sealed record CreateCategoryRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    Guid? ParentId = null,
    string? NameArabic = null);

/// <summary>Adding a brand.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The brand name.</param>
/// <param name="NameArabic">The name in Arabic.</param>
public sealed record CreateBrandRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    string? NameArabic = null);

/// <summary>Adding a warehouse.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The warehouse name.</param>
/// <param name="BranchId">The branch it belongs to, or null for a central store.</param>
/// <param name="NameArabic">The name in Arabic.</param>
/// <param name="Address">Where it is.</param>
public sealed record CreateWarehouseRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    Guid? BranchId = null,
    string? NameArabic = null,
    string? Address = null);

/// <summary>Renaming a record.</summary>
/// <param name="Name">The new name.</param>
public sealed record RenameRequest([property: JsonRequired] string Name);

/// <summary>Withdrawing a record from use, or returning it.</summary>
/// <param name="IsActive">Whether it should be usable.</param>
public sealed record SetActiveRequest([property: JsonRequired] bool IsActive);
