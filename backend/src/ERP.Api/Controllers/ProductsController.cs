using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Inventory.Products;
using ERP.Domain.Inventory;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The product master.</summary>
/// <remarks>
/// The specification puts a product on three tabs; this exposes it as one read and
/// four writes, grouped the way the tabs are. Editing a product is rarely editing all
/// of it - somebody reprices, or somebody sets reorder levels - and a single
/// replace-everything endpoint would make each of those send back the whole record and
/// risk clobbering a field a colleague changed a moment earlier.
/// <para>
/// Nothing here deletes. A product named on any document must go on meaning what it
/// meant, so withdrawal is a flag.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory/products")]
[Authorize]
[Produces("application/json")]
public sealed class ProductsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="ProductsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public ProductsController(ISender sender) => _sender = sender;

    /// <summary>Lists products.</summary>
    /// <param name="search">Matches code, description, or barcode. Omit for all.</param>
    /// <param name="categoryId">Restricts to one category. Omit for all.</param>
    /// <param name="includeInactive">Whether to include withdrawn products.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The products, by code.</returns>
    /// <remarks>
    /// Searched on the server rather than filtered in the browser: a product master
    /// runs to tens of thousands of rows, and barcode is included because scanning is
    /// how a product is most often found.
    /// </remarks>
    /// <response code="200">The products.</response>
    [HttpGet]
    [RequiresPermission("inventory", "product", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<ProductSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ProductSummary>> result = await _sender.Send(
            new ListProductsQuery(search, categoryId, includeInactive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one product in full.</summary>
    /// <param name="id">The product.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Everything the edit screen needs, including its barcodes.</returns>
    /// <response code="200">The product, with its barcodes.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("inventory", "product", "view")]
    [ProducesResponseType(typeof(ProductDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result<ProductDetail> result = await _sender.Send(
            new GetProductQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a product.</summary>
    /// <param name="request">The product.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new product's identifier.</returns>
    /// <remarks>
    /// Leave <c>code</c> blank to have the next one issued - <c>PRO-1004</c> becomes
    /// <c>PRO-1005</c> - which is how the reference application numbers products. The
    /// category and stock unit must belong to the selected firm, and the purchase and
    /// sales units must convert to the stock unit.
    /// </remarks>
    /// <response code="201">Created.</response>
    /// <response code="400">The product is invalid, or a unit does not convert.</response>
    /// <response code="404">The category, brand, or unit is not in this firm.</response>
    /// <response code="409">The code is already used.</response>
    [HttpPost]
    [RequiresPermission("inventory", "product", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateProductCommand(
                request.Description,
                request.CategoryId,
                request.StockUnitId,
                request.Code,
                request.ItemType,
                request.BrandId,
                request.PurchaseUnitId,
                request.SalesUnitId,
                request.DescriptionArabic,
                request.ShortDescription,
                request.ItemName),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Changes a product's descriptive fields — the specification's first tab.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">The descriptive fields.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// The code is not editable. It is how the product is referred to on every document
    /// already entered, and changing it would leave those documents naming something
    /// that no longer exists.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpPut("{id:guid}/description")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DescribeAsync(
        Guid id,
        [FromBody] DescribeProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new DescribeProductCommand(
                id,
                request.Description,
                request.DescriptionArabic,
                request.ShortDescription,
                request.ItemName,
                request.Manufacturer,
                request.Label,
                request.Size,
                request.Origin,
                request.Rack,
                request.Bin),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Sets what a product costs and what it sells for.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">The rate block.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// A retail rate below cost is accepted - a loss-leader is a decision, not a
    /// mistake - but a retail rate above a stated MRP is refused, because the printed
    /// price is a legal ceiling rather than a suggestion.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="400">A rate is negative, or retail exceeds the MRP.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpPut("{id:guid}/rates")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetRatesAsync(
        Guid id,
        [FromBody] SetProductRatesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetProductRatesCommand(
                id,
                request.CostingMethod,
                request.Cost,
                request.RetailRate,
                request.WholesaleRate,
                request.OtherRate,
                request.MaximumRetailPrice,
                request.ProfitPercentage,
                request.CorPercentage),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Sets how a product is stocked and tracked — the second tab.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">The units, levels, and tracking flags.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// The purchase and sales units must convert to the stock unit: buying in kilograms
    /// and stocking in litres is not a conversion anything can make, and accepting it
    /// would produce a stock figure that means nothing. Batches and serial numbers may
    /// both be set - a handset arrives in a batch and still has its own IMEI - but
    /// neither can be set on something never held in stock.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="400">A unit does not convert, or the levels do not ascend.</response>
    /// <response code="404">No such product or unit in the selected firm.</response>
    [HttpPut("{id:guid}/stocking")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStockingAsync(
        Guid id,
        [FromBody] SetProductStockingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetProductStockingCommand(
                id,
                request.PurchaseUnitId,
                request.SalesUnitId,
                request.MinimumLevel,
                request.ReorderLevel,
                request.MaximumLevel,
                request.Movement,
                request.TracksBatches,
                request.TracksSerialNumbers,
                request.ShelfLifeDays,
                request.IsPacking),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Records the mobile-device attributes.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">The attributes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Free text, deliberately: these are printed on a box and searched for as they
    /// appear there. On every product rather than only on devices, because the service
    /// module looks products up by them.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpPut("{id:guid}/device")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDeviceAsync(
        Guid id,
        [FromBody] SetProductDeviceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetProductDeviceCommand(
                id, request.Device, request.Colour, request.Battery,
                request.Ram, request.Storage),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Adds a barcode to a product.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">The barcode and, optionally, its own rates.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new barcode's identifier.</returns>
    /// <remarks>
    /// Omit the rates for a barcode that prices as the product does. A barcode carrying
    /// its own rates is the specification's multiple-rate grid: the same goods sold
    /// under another label at another price.
    /// </remarks>
    /// <response code="201">Added.</response>
    /// <response code="409">The barcode is already on this product.</response>
    [HttpPost("{id:guid}/barcodes")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddBarcodeAsync(
        Guid id,
        [FromBody] AddBarcodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new AddProductBarcodeCommand(
                id, request.Barcode, request.Cost, request.RetailRate,
                request.WholesaleRate, request.MaximumRetailPrice),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Removes a barcode from a product.</summary>
    /// <param name="id">The product.</param>
    /// <param name="barcodeId">The barcode.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Removed.</response>
    /// <response code="404">No such product or barcode.</response>
    [HttpDelete("{id:guid}/barcodes/{barcodeId:guid}")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBarcodeAsync(
        Guid id,
        Guid barcodeId,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new RemoveProductBarcodeCommand(id, barcodeId), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a product from use, or returns it.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">Whether it should be usable.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpPost("{id:guid}/active")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] SetFlagRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetProductActiveCommand(id, request.Value), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Stops the firm buying a product, or resumes.</summary>
    /// <param name="id">The product.</param>
    /// <param name="request">Whether the firm has stopped buying it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Separate from withdrawal on purpose. A discontinued product is still sold down
    /// from stock on hand; a withdrawn one is off the system. One flag for both would
    /// force a choice between hiding goods still on the shelf and offering goods
    /// nobody can get.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such product in the selected firm.</response>
    [HttpPost("{id:guid}/discontinued")]
    [RequiresPermission("inventory", "product", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDiscontinuedAsync(
        Guid id,
        [FromBody] SetFlagRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetProductDiscontinuedCommand(id, request.Value), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Adding a product.</summary>
/// <param name="Description">What it is called on a document.</param>
/// <param name="CategoryId">The category it reports under.</param>
/// <param name="StockUnitId">The unit stock is counted in.</param>
/// <param name="Code">The code, or blank to have the next one issued.</param>
/// <param name="ItemType">Whether it is stocked, a service, or non-stock.</param>
/// <param name="BrandId">The brand, where it has one.</param>
/// <param name="PurchaseUnitId">The unit it is bought in. Defaults to the stock unit.</param>
/// <param name="SalesUnitId">The unit it is sold in. Defaults to the stock unit.</param>
/// <param name="DescriptionArabic">Its description in Arabic.</param>
/// <param name="ShortDescription">The short form, for receipts.</param>
/// <param name="ItemName">The manufacturer's own name for it.</param>
public sealed record CreateProductRequest(
    [property: JsonRequired] string Description,
    [property: JsonRequired] Guid CategoryId,
    [property: JsonRequired] Guid StockUnitId,
    string? Code = null,
    ItemType ItemType = ItemType.Stock,
    Guid? BrandId = null,
    Guid? PurchaseUnitId = null,
    Guid? SalesUnitId = null,
    string? DescriptionArabic = null,
    string? ShortDescription = null,
    string? ItemName = null);

/// <summary>Changing a product's descriptive fields.</summary>
/// <param name="Description">What it is called on a document.</param>
/// <param name="DescriptionArabic">Its description in Arabic.</param>
/// <param name="ShortDescription">The short form, for receipts.</param>
/// <param name="ItemName">The manufacturer's own name for it.</param>
/// <param name="Manufacturer">The manufacturer.</param>
/// <param name="Label">The shelf label.</param>
/// <param name="Size">The size, as printed.</param>
/// <param name="Origin">The country of origin.</param>
/// <param name="Rack">The rack it is stored on.</param>
/// <param name="Bin">The bin it is stored in.</param>
public sealed record DescribeProductRequest(
    [property: JsonRequired] string Description,
    string? DescriptionArabic = null,
    string? ShortDescription = null,
    string? ItemName = null,
    string? Manufacturer = null,
    string? Label = null,
    string? Size = null,
    string? Origin = null,
    string? Rack = null,
    string? Bin = null);

/// <summary>Setting a product's rates.</summary>
/// <param name="CostingMethod">How its cost is arrived at.</param>
/// <param name="Cost">The cost of one stock unit.</param>
/// <param name="RetailRate">The retail rate.</param>
/// <param name="WholesaleRate">The wholesale rate.</param>
/// <param name="OtherRate">The third rate.</param>
/// <param name="MaximumRetailPrice">The printed MRP. Zero means none is stated.</param>
/// <param name="ProfitPercentage">The margin the sales rates are built on.</param>
/// <param name="CorPercentage">The reference application's COR percentage.</param>
public sealed record SetProductRatesRequest(
    [property: JsonRequired] CostingMethod CostingMethod,
    decimal Cost = 0m,
    decimal RetailRate = 0m,
    decimal WholesaleRate = 0m,
    decimal OtherRate = 0m,
    decimal MaximumRetailPrice = 0m,
    decimal ProfitPercentage = 0m,
    decimal CorPercentage = 0m);

/// <summary>Setting how a product is stocked and tracked.</summary>
/// <param name="PurchaseUnitId">The unit it is bought in.</param>
/// <param name="SalesUnitId">The unit it is sold in.</param>
/// <param name="MinimumLevel">The critical level.</param>
/// <param name="ReorderLevel">The level at which to raise a purchase.</param>
/// <param name="MaximumLevel">The overstocked level. Zero means no ceiling.</param>
/// <param name="Movement">How quickly it turns over.</param>
/// <param name="TracksBatches">Whether stock of it is held in batches.</param>
/// <param name="TracksSerialNumbers">Whether every unit carries its own number.</param>
/// <param name="ShelfLifeDays">How long a batch lasts. Needs batch tracking.</param>
/// <param name="IsPacking">Whether it is packaging rather than goods.</param>
public sealed record SetProductStockingRequest(
    [property: JsonRequired] Guid PurchaseUnitId,
    [property: JsonRequired] Guid SalesUnitId,
    decimal MinimumLevel = 0m,
    decimal ReorderLevel = 0m,
    decimal MaximumLevel = 0m,
    MovementClass Movement = MovementClass.Unclassified,
    bool TracksBatches = false,
    bool TracksSerialNumbers = false,
    int? ShelfLifeDays = null,
    bool IsPacking = false);

/// <summary>Recording the mobile-device attributes.</summary>
/// <param name="Device">The device model.</param>
/// <param name="Colour">Its colour.</param>
/// <param name="Battery">Its battery capacity.</param>
/// <param name="Ram">Its memory.</param>
/// <param name="Storage">Its storage.</param>
public sealed record SetProductDeviceRequest(
    string? Device = null,
    string? Colour = null,
    string? Battery = null,
    string? Ram = null,
    string? Storage = null);

/// <summary>Adding a barcode.</summary>
/// <param name="Barcode">The code as scanned.</param>
/// <param name="Cost">What this pack cost, or null to price as the product does.</param>
/// <param name="RetailRate">The retail rate for this barcode.</param>
/// <param name="WholesaleRate">The wholesale rate for this barcode.</param>
/// <param name="MaximumRetailPrice">The MRP printed for this barcode.</param>
public sealed record AddBarcodeRequest(
    [property: JsonRequired] string Barcode,
    decimal? Cost = null,
    decimal? RetailRate = null,
    decimal? WholesaleRate = null,
    decimal? MaximumRetailPrice = null);

/// <summary>Setting a boolean flag on a product.</summary>
/// <param name="Value">The new value.</param>
public sealed record SetFlagRequest([property: JsonRequired] bool Value);
