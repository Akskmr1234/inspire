using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Products;

// -------------------------------------------------------------------------- reading

/// <summary>Lists the product master.</summary>
/// <param name="Search">
/// Narrows to products whose code, description or barcode contains this. Omit for all.
/// </param>
/// <param name="CategoryId">Restricts to one category. Omit for all.</param>
/// <param name="IncludeInactive">Whether to include products withdrawn from use.</param>
/// <remarks>
/// Searched on the server rather than filtered in the browser, unlike the masters. A
/// chart of accounts is a few hundred rows; a product master is tens of thousands, and
/// sending all of them so the grid can filter three would be the wrong trade in every
/// direction.
/// </remarks>
public sealed record ListProductsQuery(
    string? Search = null,
    Guid? CategoryId = null,
    bool IncludeInactive = false) : IQuery<IReadOnlyList<ProductSummary>>;

/// <summary>A product as a list shows it.</summary>
/// <param name="Id">The product.</param>
/// <param name="Code">Its code.</param>
/// <param name="Description">What it is called on a document.</param>
/// <param name="DescriptionArabic">Its description in Arabic.</param>
/// <param name="ItemType">Whether it is stocked, a service, or non-stock.</param>
/// <param name="CategoryId">The category it reports under.</param>
/// <param name="CategoryName">The category's name, for display.</param>
/// <param name="BrandName">The brand's name, where it has one.</param>
/// <param name="StockUnitCode">The unit stock is counted in.</param>
/// <param name="Currency">The currency its rates are stated in.</param>
/// <param name="Cost">What one stock unit costs.</param>
/// <param name="RetailRate">What one stock unit retails at.</param>
/// <param name="ReorderLevel">The level at which more is ordered.</param>
/// <param name="TracksBatches">Whether stock of it is held in batches.</param>
/// <param name="TracksSerialNumbers">Whether every unit carries its own number.</param>
/// <param name="IsDiscontinued">Whether the firm has stopped buying it.</param>
/// <param name="IsActive">Whether it may be used on new documents.</param>
/// <param name="BarcodeCount">How many barcodes it carries.</param>
public sealed record ProductSummary(
    Guid Id,
    string Code,
    string Description,
    string? DescriptionArabic,
    ItemType ItemType,
    Guid CategoryId,
    string CategoryName,
    string? BrandName,
    string StockUnitCode,
    string Currency,
    decimal Cost,
    decimal RetailRate,
    decimal ReorderLevel,
    bool TracksBatches,
    bool TracksSerialNumbers,
    bool IsDiscontinued,
    bool IsActive,
    int BarcodeCount);

/// <summary>Reads one product in full, for the edit screen.</summary>
/// <param name="ProductId">The product.</param>
public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDetail>;

/// <summary>One barcode of a product.</summary>
/// <param name="Id">The barcode.</param>
/// <param name="Barcode">The code as scanned.</param>
/// <param name="Cost">What this pack cost.</param>
/// <param name="RetailRate">The retail rate for this barcode.</param>
/// <param name="WholesaleRate">The wholesale rate for this barcode.</param>
/// <param name="MaximumRetailPrice">The MRP printed for this barcode.</param>
public sealed record ProductBarcodeView(
    Guid Id,
    string Barcode,
    decimal Cost,
    decimal RetailRate,
    decimal WholesaleRate,
    decimal MaximumRetailPrice);

/// <summary>A product in full.</summary>
/// <param name="Id">The product.</param>
/// <param name="Code">Its code.</param>
/// <param name="Description">What it is called on a document.</param>
/// <param name="DescriptionArabic">Its description in Arabic.</param>
/// <param name="ShortDescription">The short form, for receipts.</param>
/// <param name="ItemName">The manufacturer's own name for it.</param>
/// <param name="Manufacturer">The manufacturer.</param>
/// <param name="Label">The shelf label.</param>
/// <param name="Size">The size, as printed.</param>
/// <param name="Origin">The country of origin.</param>
/// <param name="ItemType">Whether it is stocked, a service, or non-stock.</param>
/// <param name="CategoryId">The category it reports under.</param>
/// <param name="BrandId">The brand, where it has one.</param>
/// <param name="StockUnitId">The unit stock is counted in.</param>
/// <param name="PurchaseUnitId">The unit it is bought in.</param>
/// <param name="SalesUnitId">The unit it is sold in.</param>
/// <param name="Currency">The currency its rates are stated in.</param>
/// <param name="CostingMethod">How its cost is arrived at.</param>
/// <param name="Cost">The cost of one stock unit.</param>
/// <param name="ProfitPercentage">The margin the sales rates are built on.</param>
/// <param name="CorPercentage">The reference application's COR percentage.</param>
/// <param name="RetailRate">The retail rate.</param>
/// <param name="WholesaleRate">The wholesale rate.</param>
/// <param name="OtherRate">The third rate.</param>
/// <param name="MaximumRetailPrice">The printed MRP.</param>
/// <param name="MinimumLevel">The critical level.</param>
/// <param name="ReorderLevel">The level at which to raise a purchase.</param>
/// <param name="MaximumLevel">The overstocked level.</param>
/// <param name="Movement">How quickly it turns over.</param>
/// <param name="Device">The device model.</param>
/// <param name="Colour">Its colour.</param>
/// <param name="Battery">Its battery capacity.</param>
/// <param name="Ram">Its memory.</param>
/// <param name="Storage">Its storage.</param>
/// <param name="Rack">The rack it is stored on.</param>
/// <param name="Bin">The bin it is stored in.</param>
/// <param name="TracksBatches">Whether stock of it is held in batches.</param>
/// <param name="TracksSerialNumbers">Whether every unit carries its own number.</param>
/// <param name="ShelfLifeDays">How long a batch of it lasts.</param>
/// <param name="IsPacking">Whether it is packaging rather than goods.</param>
/// <param name="IsDiscontinued">Whether the firm has stopped buying it.</param>
/// <param name="IsActive">Whether it may be used on new documents.</param>
/// <param name="Barcodes">The barcodes it carries.</param>
public sealed record ProductDetail(
    Guid Id,
    string Code,
    string Description,
    string? DescriptionArabic,
    string? ShortDescription,
    string? ItemName,
    string? Manufacturer,
    string? Label,
    string? Size,
    string? Origin,
    ItemType ItemType,
    Guid CategoryId,
    Guid? BrandId,
    Guid StockUnitId,
    Guid PurchaseUnitId,
    Guid SalesUnitId,
    string Currency,
    CostingMethod CostingMethod,
    decimal Cost,
    decimal ProfitPercentage,
    decimal CorPercentage,
    decimal RetailRate,
    decimal WholesaleRate,
    decimal OtherRate,
    decimal MaximumRetailPrice,
    decimal MinimumLevel,
    decimal ReorderLevel,
    decimal MaximumLevel,
    MovementClass Movement,
    string? Device,
    string? Colour,
    string? Battery,
    string? Ram,
    string? Storage,
    string? Rack,
    string? Bin,
    bool TracksBatches,
    bool TracksSerialNumbers,
    int? ShelfLifeDays,
    bool IsPacking,
    bool IsDiscontinued,
    bool IsActive,
    IReadOnlyList<ProductBarcodeView> Barcodes);

/// <summary>Reads the product master.</summary>
public interface IProductReader
{
    /// <summary>Lists products matching a search.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="search">Text to match against code, description, or barcode.</param>
    /// <param name="categoryId">One category, or null for all.</param>
    /// <param name="includeInactive">Whether to include withdrawn products.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The products, by code.</returns>
    Task<IReadOnlyList<ProductSummary>> ListAsync(
        FirmId firmId,
        string? search,
        CategoryId? categoryId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one product in full.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The product, or <see langword="null"/>.</returns>
    Task<ProductDetail?> FindAsync(
        FirmId firmId,
        ProductId productId,
        CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------- writing

/// <summary>Adds a product.</summary>
/// <param name="Description">What it is called on a document.</param>
/// <param name="CategoryId">The category it reports under.</param>
/// <param name="StockUnitId">The unit stock is counted in.</param>
/// <param name="Code">
/// The code, or blank to have the next one issued. The reference application numbers
/// products that way and so does this.
/// </param>
/// <param name="ItemType">Whether it is stocked, a service, or non-stock.</param>
/// <param name="BrandId">The brand, where it has one.</param>
/// <param name="PurchaseUnitId">The unit it is bought in. Defaults to the stock unit.</param>
/// <param name="SalesUnitId">The unit it is sold in. Defaults to the stock unit.</param>
/// <param name="DescriptionArabic">Its description in Arabic.</param>
/// <param name="ShortDescription">The short form, for receipts.</param>
/// <param name="ItemName">The manufacturer's own name for it.</param>
public sealed record CreateProductCommand(
    string Description,
    Guid CategoryId,
    Guid StockUnitId,
    string? Code = null,
    ItemType ItemType = ItemType.Stock,
    Guid? BrandId = null,
    Guid? PurchaseUnitId = null,
    Guid? SalesUnitId = null,
    string? DescriptionArabic = null,
    string? ShortDescription = null,
    string? ItemName = null) : ICommand<Guid>;

/// <summary>Changes a product's descriptive fields.</summary>
/// <param name="ProductId">The product.</param>
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
public sealed record DescribeProductCommand(
    Guid ProductId,
    string Description,
    string? DescriptionArabic = null,
    string? ShortDescription = null,
    string? ItemName = null,
    string? Manufacturer = null,
    string? Label = null,
    string? Size = null,
    string? Origin = null,
    string? Rack = null,
    string? Bin = null) : ICommand;

/// <summary>Sets what a product costs and what it sells for.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="CostingMethod">How its cost is arrived at.</param>
/// <param name="Cost">The cost of one stock unit.</param>
/// <param name="RetailRate">The retail rate.</param>
/// <param name="WholesaleRate">The wholesale rate.</param>
/// <param name="OtherRate">The third rate.</param>
/// <param name="MaximumRetailPrice">The printed MRP.</param>
/// <param name="ProfitPercentage">The margin the sales rates are built on.</param>
/// <param name="CorPercentage">The reference application's COR percentage.</param>
public sealed record SetProductRatesCommand(
    Guid ProductId,
    CostingMethod CostingMethod,
    decimal Cost,
    decimal RetailRate,
    decimal WholesaleRate = 0m,
    decimal OtherRate = 0m,
    decimal MaximumRetailPrice = 0m,
    decimal ProfitPercentage = 0m,
    decimal CorPercentage = 0m) : ICommand;

/// <summary>Sets how a product is stocked and tracked.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="PurchaseUnitId">The unit it is bought in.</param>
/// <param name="SalesUnitId">The unit it is sold in.</param>
/// <param name="MinimumLevel">The critical level.</param>
/// <param name="ReorderLevel">The level at which to raise a purchase.</param>
/// <param name="MaximumLevel">The overstocked level.</param>
/// <param name="Movement">How quickly it turns over.</param>
/// <param name="TracksBatches">Whether stock of it is held in batches.</param>
/// <param name="TracksSerialNumbers">Whether every unit carries its own number.</param>
/// <param name="ShelfLifeDays">How long a batch of it lasts.</param>
/// <param name="IsPacking">Whether it is packaging rather than goods.</param>
public sealed record SetProductStockingCommand(
    Guid ProductId,
    Guid PurchaseUnitId,
    Guid SalesUnitId,
    decimal MinimumLevel = 0m,
    decimal ReorderLevel = 0m,
    decimal MaximumLevel = 0m,
    MovementClass Movement = MovementClass.Unclassified,
    bool TracksBatches = false,
    bool TracksSerialNumbers = false,
    int? ShelfLifeDays = null,
    bool IsPacking = false) : ICommand;

/// <summary>Records the mobile-device attributes.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Device">The device model.</param>
/// <param name="Colour">Its colour.</param>
/// <param name="Battery">Its battery capacity.</param>
/// <param name="Ram">Its memory.</param>
/// <param name="Storage">Its storage.</param>
public sealed record SetProductDeviceCommand(
    Guid ProductId,
    string? Device = null,
    string? Colour = null,
    string? Battery = null,
    string? Ram = null,
    string? Storage = null) : ICommand;

/// <summary>Adds a barcode to a product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Barcode">The code as scanned.</param>
/// <param name="Cost">What this pack cost, or null for the product's rates.</param>
/// <param name="RetailRate">The retail rate for this barcode.</param>
/// <param name="WholesaleRate">The wholesale rate for this barcode.</param>
/// <param name="MaximumRetailPrice">The MRP printed for this barcode.</param>
public sealed record AddProductBarcodeCommand(
    Guid ProductId,
    string Barcode,
    decimal? Cost = null,
    decimal? RetailRate = null,
    decimal? WholesaleRate = null,
    decimal? MaximumRetailPrice = null) : ICommand<Guid>;

/// <summary>Removes a barcode from a product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="BarcodeId">The barcode.</param>
public sealed record RemoveProductBarcodeCommand(Guid ProductId, Guid BarcodeId) : ICommand;

/// <summary>Withdraws a product from use, or returns it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="IsActive">Whether it should be usable.</param>
public sealed record SetProductActiveCommand(Guid ProductId, bool IsActive) : ICommand;

/// <summary>Stops the firm buying a product, or resumes.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="IsDiscontinued">Whether the firm has stopped buying it.</param>
/// <remarks>
/// Deliberately separate from withdrawal. A discontinued product is still sold down
/// from stock on hand; a withdrawn one is off the system. One flag for both would
/// force a choice between hiding goods still on the shelf and offering goods nobody
/// can get.
/// </remarks>
public sealed record SetProductDiscontinuedCommand(
    Guid ProductId,
    bool IsDiscontinued) : ICommand;

/// <summary>Validates a <see cref="CreateProductCommand"/>.</summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateProductCommandValidator"/> class.</summary>
    public CreateProductCommandValidator()
    {
        RuleFor(c => c.Description).NotEmpty()
            .MaximumLength(Product.MaximumDescriptionLength);
        RuleFor(c => c.CategoryId).NotEmpty();
        RuleFor(c => c.StockUnitId).NotEmpty();

        // Only checked when supplied. Blank is the signal to issue the next one.
        RuleFor(c => c.Code!).MaximumLength(Product.MaximumCodeLength)
            .When(c => !string.IsNullOrWhiteSpace(c.Code));
    }
}

/// <summary>Validates a <see cref="DescribeProductCommand"/>.</summary>
public sealed class DescribeProductCommandValidator
    : AbstractValidator<DescribeProductCommand>
{
    /// <summary>Initialises a new instance of the <see cref="DescribeProductCommandValidator"/> class.</summary>
    public DescribeProductCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Description).NotEmpty()
            .MaximumLength(Product.MaximumDescriptionLength);
    }
}

/// <summary>Validates a <see cref="AddProductBarcodeCommand"/>.</summary>
public sealed class AddProductBarcodeCommandValidator
    : AbstractValidator<AddProductBarcodeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="AddProductBarcodeCommandValidator"/> class.</summary>
    public AddProductBarcodeCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Barcode).NotEmpty().MaximumLength(ProductBarcode.MaximumLength);
    }
}
