using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Products;

/// <summary>Shared resolution for the product handlers.</summary>
internal static class ProductContext
{
    /// <summary>The prefix a firm's product sequence runs under.</summary>
    /// <remarks>
    /// Matches the reference application's own, so a migrated firm's codes continue
    /// rather than restarting alongside them.
    /// </remarks>
    internal const string CodePrefix = "PRO-";

    /// <summary>Resolves the firm a product is being read or written in.</summary>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <returns>The firm, or the reason it could not be resolved.</returns>
    internal static Result<FirmId> ResolveFirm(ITenantContext tenantContext) =>
        tenantContext.FirmId is { } firmId
            ? Result.Success(firmId)
            : Result.Failure<FirmId>(Error.Forbidden(
                "Product.NoFirmSelected", "A firm must be selected to work with products."));

    /// <summary>Resolves a product, refusing one belonging to another firm.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="productId">The product being acted on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The product, or the reason it could not be resolved.</returns>
    internal static async Task<Result<Product>> ResolveAsync(
        IProductRepository products,
        ITenantContext tenantContext,
        Guid productId,
        CancellationToken cancellationToken)
    {
        Result<FirmId> firm = ResolveFirm(tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<Product>(firm.Error);
        }

        Product? product = await products.FindAsync(
            ProductId.From(productId), cancellationToken);

        return product is null || product.FirmId != firm.Value
            ? Result.Failure<Product>(Error.NotFound(
                "Product.NotFound", "No such product in the selected firm."))
            : Result.Success(product);
    }
}

/// <summary>Handles the two product read queries.</summary>
public sealed class ProductQueryHandler
    : IQueryHandler<ListProductsQuery, IReadOnlyList<ProductSummary>>,
      IQueryHandler<GetProductQuery, ProductDetail>
{
    private readonly IProductReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="ProductQueryHandler"/> class.</summary>
    /// <param name="reader">The product reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public ProductQueryHandler(IProductReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ProductSummary>>> Handle(
        ListProductsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = ProductContext.ResolveFirm(_tenantContext);

        return firm.IsFailure
            ? Result.Failure<IReadOnlyList<ProductSummary>>(firm.Error)
            : Result.Success(await _reader.ListAsync(
                firm.Value,
                request.Search,
                request.CategoryId is { } id ? CategoryId.From(id) : null,
                request.IncludeInactive,
                cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<ProductDetail>> Handle(
        GetProductQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> firm = ProductContext.ResolveFirm(_tenantContext);

        if (firm.IsFailure)
        {
            return Result.Failure<ProductDetail>(firm.Error);
        }

        ProductDetail? product = await _reader.FindAsync(
            firm.Value, ProductId.From(request.ProductId), cancellationToken);

        return product is null
            ? Result.Failure<ProductDetail>(Error.NotFound(
                "Product.NotFound", "No such product in the selected firm."))
            : Result.Success(product);
    }
}

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _products;
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IFirmRepository _firms;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateProductCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="firms">The firm repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateProductCommandHandler(
        IProductRepository products,
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IFirmRepository firms,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _masters = masters;
        _tenantContext = tenantContext;
        _firms = firms;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FirmId> resolved = ProductContext.ResolveFirm(_tenantContext);

        if (resolved.IsFailure)
        {
            return Result.Failure<Guid>(resolved.Error);
        }

        FirmId firmId = resolved.Value;

        Domain.Tenancy.Firm? firm = await _firms.FindAsync(firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        Category? category = await _masters.FindCategoryAsync(
            CategoryId.From(request.CategoryId), cancellationToken);

        if (category is null || category.FirmId != firmId)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Product.CategoryNotFound", "No such category in the selected firm."));
        }

        UnitOfMeasure? stockUnit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(request.StockUnitId), cancellationToken);

        if (stockUnit is null || stockUnit.FirmId != firmId)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Product.UnitNotFound", "No such unit in the selected firm."));
        }

        // Blank means "issue the next one", which is how the reference application
        // numbers products. A supplied code is checked for a clash here so the refusal
        // names the code rather than surfacing as a unique-index violation.
        string code = string.IsNullOrWhiteSpace(request.Code)
            ? await _products.NextCodeAsync(firmId, ProductContext.CodePrefix, cancellationToken)
            : request.Code.Trim().ToUpperInvariant();

        if (await _products.CodeExistsAsync(firmId, code, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Product.CodeTaken", $"'{code}' is already used by another product."));
        }

        // The product's currency is the firm's. A per-product currency is a Phase 2
        // concern - multi-currency pricing needs a rate table and a decision about
        // which rate applies when - and defaulting to the firm's is right until then.
        Result<Product> created = Product.Create(
            category, stockUnit, code, request.Description, request.ItemType,
            firm.BaseCurrency);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Product product = created.Value;

        Result described = product.Describe(
            request.Description, request.ShortDescription, request.ItemName);

        if (described.IsFailure)
        {
            return Result.Failure<Guid>(described.Error);
        }

        product.SetArabicDescription(request.DescriptionArabic);

        if (request.BrandId is { } brandId)
        {
            Brand? brand = await _masters.FindBrandAsync(
                BrandId.From(brandId), cancellationToken);

            if (brand is null || brand.FirmId != firmId)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "Product.BrandNotFound", "No such brand in the selected firm."));
            }

            Result branded = product.SetBrand(brand);

            if (branded.IsFailure)
            {
                return Result.Failure<Guid>(branded.Error);
            }
        }

        // Only touched when a trading unit differs from the stock unit; the aggregate
        // already defaults both to it.
        if (request.PurchaseUnitId is not null || request.SalesUnitId is not null)
        {
            Result<UnitOfMeasure> purchase = await ResolveUnitAsync(
                request.PurchaseUnitId, stockUnit, firmId, cancellationToken);

            if (purchase.IsFailure)
            {
                return Result.Failure<Guid>(purchase.Error);
            }

            Result<UnitOfMeasure> sales = await ResolveUnitAsync(
                request.SalesUnitId, stockUnit, firmId, cancellationToken);

            if (sales.IsFailure)
            {
                return Result.Failure<Guid>(sales.Error);
            }

            Result units = product.SetUnits(purchase.Value, sales.Value, stockUnit);

            if (units.IsFailure)
            {
                return Result.Failure<Guid>(units.Error);
            }
        }

        _products.Add(product);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(product.Id.Value);
    }

    private async Task<Result<UnitOfMeasure>> ResolveUnitAsync(
        Guid? unitId,
        UnitOfMeasure fallback,
        FirmId firmId,
        CancellationToken cancellationToken)
    {
        if (unitId is not { } id)
        {
            return Result.Success(fallback);
        }

        UnitOfMeasure? unit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(id), cancellationToken);

        return unit is null || unit.FirmId != firmId
            ? Result.Failure<UnitOfMeasure>(Error.NotFound(
                "Product.UnitNotFound", "No such unit in the selected firm."))
            : Result.Success(unit);
    }
}

/// <summary>Handles <see cref="DescribeProductCommand"/>.</summary>
public sealed class DescribeProductCommandHandler : ICommandHandler<DescribeProductCommand>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="DescribeProductCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public DescribeProductCommandHandler(
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        DescribeProductCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Product product = found.Value;

        Result described = product.Describe(
            request.Description, request.ShortDescription, request.ItemName);

        if (described.IsFailure)
        {
            return described;
        }

        product.SetArabicDescription(request.DescriptionArabic);

        Result attributes = product.SetAttributes(
            request.Manufacturer, request.Label, request.Size, request.Origin);

        if (attributes.IsFailure)
        {
            return attributes;
        }

        Result location = product.SetLocation(request.Rack, request.Bin);

        if (location.IsFailure)
        {
            return location;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="SetProductRatesCommand"/>.</summary>
public sealed class SetProductRatesCommandHandler : ICommandHandler<SetProductRatesCommand>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetProductRatesCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetProductRatesCommandHandler(
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetProductRatesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        // The rate block validates itself - negative figures, and a retail rate above a
        // stated MRP - so a bad set is refused before the product ever sees it.
        Result<ProductRates> rates = ProductRates.Create(
            request.Cost,
            request.ProfitPercentage,
            request.CorPercentage,
            request.RetailRate,
            request.WholesaleRate,
            request.OtherRate,
            request.MaximumRetailPrice);

        if (rates.IsFailure)
        {
            return Result.Failure(rates.Error);
        }

        Result method = found.Value.SetCostingMethod(request.CostingMethod);

        if (method.IsFailure)
        {
            return method;
        }

        found.Value.SetRates(rates.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="SetProductStockingCommand"/>.</summary>
public sealed class SetProductStockingCommandHandler
    : ICommandHandler<SetProductStockingCommand>
{
    private readonly IProductRepository _products;
    private readonly IInventoryMasterRepository _masters;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetProductStockingCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetProductStockingCommandHandler(
        IProductRepository products,
        IInventoryMasterRepository masters,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _masters = masters;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetProductStockingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Product product = found.Value;

        UnitOfMeasure? stockUnit = await _masters.FindUnitAsync(
            product.StockUnitId, cancellationToken);
        UnitOfMeasure? purchaseUnit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(request.PurchaseUnitId), cancellationToken);
        UnitOfMeasure? salesUnit = await _masters.FindUnitAsync(
            UnitOfMeasureId.From(request.SalesUnitId), cancellationToken);

        if (stockUnit is null || purchaseUnit is null || salesUnit is null)
        {
            return Result.Failure(Error.NotFound(
                "Product.UnitNotFound", "No such unit in the selected firm."));
        }

        // The aggregate checks the conversion group, which is the rule that matters:
        // buying in kilograms and stocking in litres is not a conversion anything can
        // make.
        Result units = product.SetUnits(purchaseUnit, salesUnit, stockUnit);

        if (units.IsFailure)
        {
            return units;
        }

        Result<StockLevels> levels = StockLevels.Create(
            request.MinimumLevel, request.ReorderLevel, request.MaximumLevel);

        if (levels.IsFailure)
        {
            return Result.Failure(levels.Error);
        }

        product.SetLevels(levels.Value);

        Result movement = product.SetMovement(request.Movement);

        if (movement.IsFailure)
        {
            return movement;
        }

        Result tracking = product.SetTracking(
            request.TracksBatches, request.TracksSerialNumbers, request.ShelfLifeDays);

        if (tracking.IsFailure)
        {
            return tracking;
        }

        product.SetPacking(request.IsPacking);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="SetProductDeviceCommand"/>.</summary>
public sealed class SetProductDeviceCommandHandler : ICommandHandler<SetProductDeviceCommand>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetProductDeviceCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetProductDeviceCommandHandler(
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetProductDeviceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Result<DeviceAttributes> device = DeviceAttributes.Create(
            request.Device, request.Colour, request.Battery, request.Ram, request.Storage);

        if (device.IsFailure)
        {
            return Result.Failure(device.Error);
        }

        found.Value.SetDeviceAttributes(device.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="AddProductBarcodeCommand"/>.</summary>
public sealed class AddProductBarcodeCommandHandler
    : ICommandHandler<AddProductBarcodeCommand, Guid>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="AddProductBarcodeCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public AddProductBarcodeCommandHandler(
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        AddProductBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<Guid>(found.Error);
        }

        Product product = found.Value;

        // Null throughout means "price as the product does", so a barcode with no rates
        // of its own goes on tracking a repricing rather than freezing what it was
        // created with.
        ProductRates? rates = null;

        if (request.Cost is not null
            || request.RetailRate is not null
            || request.WholesaleRate is not null
            || request.MaximumRetailPrice is not null)
        {
            Result<ProductRates> built = ProductRates.Create(
                request.Cost ?? product.Rates.Cost,
                product.Rates.ProfitPercentage,
                product.Rates.CorPercentage,
                request.RetailRate ?? product.Rates.RetailRate,
                request.WholesaleRate ?? product.Rates.WholesaleRate,
                product.Rates.OtherRate,
                request.MaximumRetailPrice ?? product.Rates.MaximumRetailPrice);

            if (built.IsFailure)
            {
                return Result.Failure<Guid>(built.Error);
            }

            rates = built.Value;
        }

        Result<ProductBarcode> added = product.AddBarcode(request.Barcode, rates);

        if (added.IsFailure)
        {
            return Result.Failure<Guid>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(added.Value.Id.Value);
    }
}

/// <summary>Handles the three product flag commands.</summary>
/// <remarks>
/// Grouped because each is one call on the aggregate wrapped in the same resolution.
/// Three classes would be three copies of the same eight lines.
/// </remarks>
public sealed class ProductStateCommandHandler
    : ICommandHandler<RemoveProductBarcodeCommand>,
      ICommandHandler<SetProductActiveCommand>,
      ICommandHandler<SetProductDiscontinuedCommand>
{
    private readonly IProductRepository _products;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ProductStateCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ProductStateCommandHandler(
        IProductRepository products,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        RemoveProductBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ApplyAsync(
            request.ProductId,
            product => product.RemoveBarcode(ProductBarcodeId.From(request.BarcodeId)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetProductActiveCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ApplyAsync(
            request.ProductId,
            product =>
            {
                if (request.IsActive)
                {
                    product.Activate();
                }
                else
                {
                    product.Deactivate();
                }

                return Result.Success();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetProductDiscontinuedCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ApplyAsync(
            request.ProductId,
            product =>
            {
                if (request.IsDiscontinued)
                {
                    product.Discontinue();
                }
                else
                {
                    product.Reinstate();
                }

                return Result.Success();
            },
            cancellationToken);
    }

    private async Task<Result> ApplyAsync(
        Guid productId,
        Func<Product, Result> change,
        CancellationToken cancellationToken)
    {
        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, productId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Result applied = change(found.Value);

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
