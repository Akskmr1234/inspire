using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Inventory;

/// <summary>
/// A thing the firm buys, holds, or sells: the product master everything in
/// inventory, sales, purchase, and service hangs off.
/// </summary>
/// <remarks>
/// <para>
/// The largest master in the specification - three tabs of it - and the one every
/// other module reaches for. What is here is what a product <em>is</em>: how it is
/// identified, what it is classified under, the units it is handled in, what it costs
/// and sells for, and how closely it is tracked. What a product <em>has</em> - stock
/// on hand, batches, serial numbers, images - is not, because none of that is true of
/// the product itself. Stock is a fact about a warehouse on a date.
/// </para>
/// <para>
/// Barcodes are the one child collection, because the specification's multiple-rate
/// barcode grid gives each its own rates and a barcode with rates is meaningless
/// apart from the product it prices. They are saved with it or not at all.
/// </para>
/// <para>
/// Scoped to the firm rather than the tenant, like every other inventory master. Two
/// companies under one group buy different things at different prices, and a shared
/// product list would make one of them wrong about both.
/// </para>
/// </remarks>
public sealed class Product : AggregateRoot<ProductId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a product code may be.</summary>
    public const int MaximumCodeLength = 40;

    /// <summary>The longest a product description may be.</summary>
    public const int MaximumDescriptionLength = 200;

    /// <summary>The longest a short description may be.</summary>
    public const int MaximumShortDescriptionLength = 100;

    /// <summary>The longest any of the free-text descriptive fields may be.</summary>
    public const int MaximumAttributeLength = 100;

    private readonly List<ProductBarcode> _barcodes = [];

    private Product(
        ProductId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string description,
        ItemType itemType,
        CategoryId categoryId,
        UnitOfMeasureId stockUnitId,
        CurrencyCode currency)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Description = description;
        ItemType = itemType;
        CategoryId = categoryId;
        StockUnitId = stockUnitId;
        PurchaseUnitId = stockUnitId;
        SalesUnitId = stockUnitId;
        Currency = currency;
        Rates = ProductRates.Empty;
        Levels = StockLevels.None;
        Device = DeviceAttributes.None;
        CostingMethod = CostingMethod.LastPurchaseRate;
        Movement = MovementClass.Unclassified;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Product()
    {
        Code = string.Empty;
        Description = string.Empty;
        Rates = ProductRates.Empty;
        Levels = StockLevels.None;
        Device = DeviceAttributes.None;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    /// <remarks>
    /// Supplied rather than generated here. The reference application issues the next
    /// number when the field is left blank, and that sequence is a numbering series
    /// like any other - owned by the application layer, which already reserves
    /// document numbers under a lock. Generating it in the domain would mean the
    /// aggregate reaching for a counter it cannot see.
    /// </remarks>
    public string Code { get; private set; }

    /// <summary>Gets the description: what the product is called on a document.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the description in Arabic, for RTL presentation.</summary>
    public string? DescriptionArabic { get; private set; }

    /// <summary>Gets the short description, for narrow layouts and receipts.</summary>
    public string? ShortDescription { get; private set; }

    /// <summary>Gets the manufacturer's own name for the item.</summary>
    public string? ItemName { get; private set; }

    /// <summary>Gets the manufacturer.</summary>
    public string? Manufacturer { get; private set; }

    /// <summary>Gets the shelf label.</summary>
    public string? Label { get; private set; }

    /// <summary>Gets the size, as printed.</summary>
    public string? Size { get; private set; }

    /// <summary>Gets the country of origin.</summary>
    public string? Origin { get; private set; }

    /// <summary>Gets what kind of thing this is.</summary>
    public ItemType ItemType { get; private set; }

    /// <summary>Gets the category it reports under.</summary>
    public CategoryId CategoryId { get; private set; }

    /// <summary>Gets the brand, if it has one.</summary>
    public BrandId? BrandId { get; private set; }

    /// <summary>Gets the supplier it is ordinarily bought from.</summary>
    /// <remarks>
    /// A ledger rather than a separate supplier table: a supplier is a sub-ledger, and
    /// giving products their own notion of one would mean two lists to keep in step.
    /// </remarks>
    public LedgerId? DefaultSupplierLedgerId { get; private set; }

    /// <summary>Gets the unit stock is counted and valued in.</summary>
    /// <remarks>
    /// The base for everything else. Purchase and sales units convert to it, so it is
    /// the one unit a stock figure can be stated in without ambiguity.
    /// </remarks>
    public UnitOfMeasureId StockUnitId { get; private set; }

    /// <summary>Gets the unit it is ordinarily bought in.</summary>
    public UnitOfMeasureId PurchaseUnitId { get; private set; }

    /// <summary>Gets the unit it is ordinarily sold in.</summary>
    public UnitOfMeasureId SalesUnitId { get; private set; }

    /// <summary>Gets the currency its rates are stated in.</summary>
    /// <remarks>
    /// Held once for the whole rate block rather than on each rate. Seven currencies
    /// on one product would be seven chances for it to disagree with itself about
    /// what it is priced in.
    /// </remarks>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the rate block.</summary>
    public ProductRates Rates { get; private set; }

    /// <summary>Gets how its cost is arrived at.</summary>
    public CostingMethod CostingMethod { get; private set; }

    /// <summary>Gets the reorder thresholds.</summary>
    public StockLevels Levels { get; private set; }

    /// <summary>Gets how quickly it turns over.</summary>
    public MovementClass Movement { get; private set; }

    /// <summary>Gets the mobile-device attributes.</summary>
    public DeviceAttributes Device { get; private set; }

    /// <summary>Gets whether stock of it is tracked in batches.</summary>
    public bool TracksBatches { get; private set; }

    /// <summary>Gets whether every unit carries its own serial or IMEI number.</summary>
    /// <remarks>
    /// Independent of <see cref="TracksBatches"/> rather than exclusive with it. A
    /// handset arrives in a batch and still has an IMEI of its own, and the service
    /// module is built on being able to find one.
    /// </remarks>
    public bool TracksSerialNumbers { get; private set; }

    /// <summary>Gets how many days after production a batch expires, if it does.</summary>
    public int? ShelfLifeDays { get; private set; }

    /// <summary>Gets whether the item is packing material rather than goods.</summary>
    public bool IsPacking { get; private set; }

    /// <summary>Gets the rack it is stored on.</summary>
    public string? Rack { get; private set; }

    /// <summary>Gets the bin it is stored in.</summary>
    public string? Bin { get; private set; }

    /// <summary>Gets whether it may still be bought and sold.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets whether it has been withdrawn from the range.</summary>
    /// <remarks>
    /// Distinct from inactive, and both are needed. A discontinued product may still
    /// be sold down from stock; an inactive one may not be transacted at all.
    /// Collapsing them would force a choice between selling remaining stock and
    /// keeping it off new orders.
    /// </remarks>
    public bool IsDiscontinued { get; private set; }

    /// <summary>Gets the barcodes it may be found by.</summary>
    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? DeletedBy { get; private set; }

    /// <summary>Gets the cost, as money.</summary>
    public Money Cost => Money.Of(Rates.Cost, Currency);

    /// <summary>Gets the retail rate, as money.</summary>
    public Money RetailRate => Money.Of(Rates.RetailRate, Currency);

    /// <summary>Gets the wholesale rate, as money.</summary>
    public Money WholesaleRate => Money.Of(Rates.WholesaleRate, Currency);

    /// <summary>Gets the maximum retail price, as money.</summary>
    public Money MaximumRetailPrice => Money.Of(Rates.MaximumRetailPrice, Currency);

    /// <summary>Gets whether the product may be transacted at all.</summary>
    public bool IsTransactable => IsActive && !IsDeleted;

    /// <summary>Gets whether stock of it is held, counted, and valued.</summary>
    /// <remarks>
    /// Only stock items are. A service has no quantity to hold, and a non-stock item
    /// is written off on receipt - putting either into a stock ledger would produce a
    /// balance that never reconciles with anything countable.
    /// </remarks>
    public bool IsStocked => ItemType == ItemType.Stock;

    /// <summary>Creates a product.</summary>
    /// <param name="category">The category it reports under.</param>
    /// <param name="stockUnit">The unit stock is counted in.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="description">What it is called on a document.</param>
    /// <param name="itemType">Whether it is stocked, a service, or non-stock.</param>
    /// <param name="currency">The currency its rates are stated in.</param>
    /// <returns>The product, or a validation failure.</returns>
    /// <remarks>
    /// The category and unit are passed as objects rather than identifiers so their
    /// firm can be checked here. Taking bare identifiers would let a product be filed
    /// under a sibling firm's category - which no tenant filter catches, because the
    /// firms share a tenant, and which would put the product on the wrong company's
    /// stock report.
    /// </remarks>
    public static Result<Product> Create(
        Category category,
        UnitOfMeasure stockUnit,
        string code,
        string description,
        ItemType itemType,
        CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(stockUnit);

        if (!Enum.IsDefined(itemType))
        {
            return Result.Failure<Product>(Error.Validation(
                "Product.UnknownItemType", $"'{itemType}' is not a recognised item type."));
        }

        Result validation = ValidateIdentity(code, description);

        if (validation.IsFailure)
        {
            return Result.Failure<Product>(validation.Error);
        }

        if (stockUnit.FirmId != category.FirmId)
        {
            return Result.Failure<Product>(Error.Validation(
                "Product.UnitFromAnotherFirm",
                $"Unit '{stockUnit.Name}' belongs to a different firm from category " +
                $"'{category.Name}'."));
        }

        if (!category.IsActive)
        {
            return Result.Failure<Product>(Error.Validation(
                "Product.CategoryInactive",
                $"Category '{category.Name}' has been deactivated and cannot take new " +
                $"products."));
        }

        return Result.Success(new Product(
            ProductId.NewId(),
            category.TenantId,
            category.FirmId,
            code.Trim().ToUpperInvariant(),
            description.Trim(),
            itemType,
            category.Id,
            stockUnit.Id,
            currency));
    }

    /// <summary>Changes what the product is called and how it is described.</summary>
    /// <param name="description">What it is called on a document.</param>
    /// <param name="shortDescription">The short form, for receipts.</param>
    /// <param name="itemName">The manufacturer's own name for it.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Describe(string description, string? shortDescription, string? itemName)
    {
        Result validation = ValidateIdentity(Code, description);

        if (validation.IsFailure)
        {
            return validation;
        }

        if (shortDescription?.Trim().Length > MaximumShortDescriptionLength)
        {
            return Result.Failure(Error.Validation(
                "Product.ShortDescriptionTooLong",
                $"A short description cannot exceed {MaximumShortDescriptionLength} " +
                $"characters."));
        }

        if (itemName?.Trim().Length > MaximumAttributeLength)
        {
            return Result.Failure(Error.Validation(
                "Product.ItemNameTooLong",
                $"An item name cannot exceed {MaximumAttributeLength} characters."));
        }

        Description = description.Trim();
        ShortDescription = Clean(shortDescription);
        ItemName = Clean(itemName);

        return Result.Success();
    }

    /// <summary>Sets the Arabic description shown in RTL mode.</summary>
    /// <param name="descriptionArabic">The Arabic description, or null to clear it.</param>
    public void SetArabicDescription(string? descriptionArabic) =>
        DescriptionArabic = Clean(descriptionArabic);

    /// <summary>Records the descriptive attributes printed on the pack.</summary>
    /// <param name="manufacturer">The manufacturer.</param>
    /// <param name="label">The shelf label.</param>
    /// <param name="size">The size, as printed.</param>
    /// <param name="origin">The country of origin.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetAttributes(
        string? manufacturer,
        string? label,
        string? size,
        string? origin)
    {
        (string Name, string? Value)[] attributes =
        [
            ("manufacturer", manufacturer),
            ("label", label),
            ("size", size),
            ("origin", origin),
        ];

        foreach ((string name, string? value) in attributes)
        {
            if (value?.Trim().Length > MaximumAttributeLength)
            {
                return Result.Failure(Error.Validation(
                    "Product.AttributeTooLong",
                    $"A product's {name} cannot exceed {MaximumAttributeLength} characters."));
            }
        }

        Manufacturer = Clean(manufacturer);
        Label = Clean(label);
        Size = Clean(size);
        Origin = Clean(origin);

        return Result.Success();
    }

    /// <summary>Files the product under a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result ReclassifyTo(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "Product.CategoryFromAnotherFirm",
                $"Category '{category.Name}' belongs to a different firm."));
        }

        if (!category.IsActive)
        {
            return Result.Failure(Error.Validation(
                "Product.CategoryInactive",
                $"Category '{category.Name}' has been deactivated."));
        }

        CategoryId = category.Id;

        return Result.Success();
    }

    /// <summary>Assigns the brand, or clears it.</summary>
    /// <param name="brand">The brand, or null to clear it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetBrand(Brand? brand)
    {
        if (brand is null)
        {
            BrandId = null;

            return Result.Success();
        }

        if (brand.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "Product.BrandFromAnotherFirm",
                $"Brand '{brand.Name}' belongs to a different firm."));
        }

        BrandId = brand.Id;

        return Result.Success();
    }

    /// <summary>Records the supplier the product is ordinarily bought from.</summary>
    /// <param name="supplier">The supplier's ledger, or null to clear it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The ledger is passed rather than its identifier so its firm and kind can be
    /// checked. A product defaulting to a sibling firm's supplier would put that firm
    /// on a purchase order, and one defaulting to a cash account would put a till on
    /// it - neither of which any later screen would think to question.
    /// </remarks>
    public Result SetDefaultSupplier(Ledger? supplier)
    {
        if (supplier is null)
        {
            DefaultSupplierLedgerId = null;

            return Result.Success();
        }

        if (supplier.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "Product.SupplierFromAnotherFirm",
                $"Ledger '{supplier.Name}' belongs to a different firm."));
        }

        if (supplier.Kind != LedgerKind.Supplier)
        {
            return Result.Failure(Error.Validation(
                "Product.NotASupplier",
                $"'{supplier.Name}' is not a supplier ledger, so it cannot be a product's " +
                $"default supplier."));
        }

        DefaultSupplierLedgerId = supplier.Id;

        return Result.Success();
    }

    /// <summary>Sets the units the product is bought and sold in.</summary>
    /// <param name="purchaseUnit">The unit it is bought in.</param>
    /// <param name="salesUnit">The unit it is sold in.</param>
    /// <param name="stockUnit">
    /// The unit stock is counted in, for checking the other two convert to it.
    /// </param>
    /// <returns>Success, or the reason the combination was refused.</returns>
    /// <remarks>
    /// Every unit must sit in the same conversion group as the stock unit, because a
    /// purchase in one unit has to become a stock figure in another and there is no
    /// factor between groups. Buying in kilograms and stocking in litres is not a
    /// conversion the system can make, and accepting it would produce a stock
    /// quantity that means nothing.
    /// </remarks>
    public Result SetUnits(
        UnitOfMeasure purchaseUnit,
        UnitOfMeasure salesUnit,
        UnitOfMeasure stockUnit)
    {
        ArgumentNullException.ThrowIfNull(purchaseUnit);
        ArgumentNullException.ThrowIfNull(salesUnit);
        ArgumentNullException.ThrowIfNull(stockUnit);

        if (stockUnit.Id != StockUnitId)
        {
            return Result.Failure(Error.Validation(
                "Product.WrongStockUnit",
                $"'{stockUnit.Name}' is not this product's stock unit."));
        }

        foreach ((string role, UnitOfMeasure unit) in
            new[] { ("purchase", purchaseUnit), ("sales", salesUnit) })
        {
            if (unit.FirmId != FirmId)
            {
                return Result.Failure(Error.Validation(
                    "Product.UnitFromAnotherFirm",
                    $"The {role} unit '{unit.Name}' belongs to a different firm."));
            }

            if (unit.GroupId != stockUnit.GroupId)
            {
                return Result.Failure(Error.Validation(
                    "Product.UnitNotConvertible",
                    $"The {role} unit '{unit.Name}' does not convert to the stock unit " +
                    $"'{stockUnit.Name}'. They are in different unit groups, and there is " +
                    $"no factor between them."));
            }
        }

        PurchaseUnitId = purchaseUnit.Id;
        SalesUnitId = salesUnit.Id;

        return Result.Success();
    }

    /// <summary>Replaces the rate block.</summary>
    /// <param name="rates">The new rates.</param>
    public void SetRates(ProductRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        Rates = rates;
    }

    /// <summary>Sets how the product's cost is arrived at.</summary>
    /// <param name="method">The costing method.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetCostingMethod(CostingMethod method) =>
        Enum.IsDefined(method)
            ? Apply(() => CostingMethod = method)
            : Result.Failure(Error.Validation(
                "Product.UnknownCostingMethod",
                $"'{method}' is not a recognised costing method."));

    /// <summary>Replaces the reorder thresholds.</summary>
    /// <param name="levels">The new levels.</param>
    public void SetLevels(StockLevels levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        Levels = levels;
    }

    /// <summary>Classifies how quickly the product turns over.</summary>
    /// <param name="movement">The movement class.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetMovement(MovementClass movement) =>
        Enum.IsDefined(movement)
            ? Apply(() => Movement = movement)
            : Result.Failure(Error.Validation(
                "Product.UnknownMovementClass",
                $"'{movement}' is not a recognised movement class."));

    /// <summary>Replaces the mobile-device attributes.</summary>
    /// <param name="device">The attributes.</param>
    public void SetDeviceAttributes(DeviceAttributes device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Device = device;
    }

    /// <summary>Sets where the product is stored.</summary>
    /// <param name="rack">The rack.</param>
    /// <param name="bin">The bin.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetLocation(string? rack, string? bin)
    {
        if (rack?.Trim().Length > MaximumAttributeLength
            || bin?.Trim().Length > MaximumAttributeLength)
        {
            return Result.Failure(Error.Validation(
                "Product.LocationTooLong",
                $"A rack or bin cannot exceed {MaximumAttributeLength} characters."));
        }

        Rack = Clean(rack);
        Bin = Clean(bin);

        return Result.Success();
    }

    /// <summary>Marks the product as packing material rather than goods.</summary>
    /// <param name="isPacking">Whether it is packing material.</param>
    public void SetPacking(bool isPacking) => IsPacking = isPacking;

    /// <summary>Sets how closely stock of the product is tracked.</summary>
    /// <param name="tracksBatches">Whether stock is held in batches.</param>
    /// <param name="tracksSerialNumbers">Whether every unit carries a serial number.</param>
    /// <param name="shelfLifeDays">
    /// How many days after production a batch expires, or null when it does not.
    /// </param>
    /// <returns>Success, or the reason the combination was refused.</returns>
    /// <remarks>
    /// Only a stocked item can be tracked. A service has no unit to carry a serial
    /// number and no batch to expire, and letting one claim otherwise would put rows
    /// in the batch ledger that no physical thing corresponds to.
    /// <para>
    /// A shelf life without batch tracking is refused for the same reason: expiry is a
    /// property of a batch, so there would be nothing for the date to attach to and
    /// the expiry report would have nothing to list.
    /// </para>
    /// </remarks>
    public Result SetTracking(
        bool tracksBatches,
        bool tracksSerialNumbers,
        int? shelfLifeDays = null)
    {
        if (!IsStocked && (tracksBatches || tracksSerialNumbers))
        {
            return Result.Failure(Error.Validation(
                "Product.TrackingNeedsStock",
                $"'{Description}' is a {ItemType.ToString().ToLowerInvariant()} item, so " +
                $"there is no physical unit to track."));
        }

        if (shelfLifeDays is <= 0)
        {
            return Result.Failure(Error.Validation(
                "Product.ShelfLifeNotPositive",
                "A shelf life must be a positive number of days."));
        }

        if (shelfLifeDays is not null && !tracksBatches)
        {
            return Result.Failure(Error.Validation(
                "Product.ShelfLifeNeedsBatches",
                "Expiry is a property of a batch. Track the product in batches, or leave " +
                "the shelf life unset."));
        }

        TracksBatches = tracksBatches;
        TracksSerialNumbers = tracksSerialNumbers;
        ShelfLifeDays = shelfLifeDays;

        return Result.Success();
    }

    /// <summary>Adds a barcode the product may be found by.</summary>
    /// <param name="barcode">The barcode as scanned.</param>
    /// <param name="rates">The rates that barcode prices at, or null for the product's.</param>
    /// <returns>The barcode, or the reason it was refused.</returns>
    /// <remarks>
    /// Duplicates within the product are refused here; duplicates across products are
    /// a unique index's job, because this aggregate cannot see them.
    /// </remarks>
    public Result<ProductBarcode> AddBarcode(string barcode, ProductRates? rates = null)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Result.Failure<ProductBarcode>(Error.Validation(
                "Product.BarcodeRequired", "A barcode is required."));
        }

        string scanned = barcode.Trim();

        if (scanned.Length > ProductBarcode.MaximumLength)
        {
            return Result.Failure<ProductBarcode>(Error.Validation(
                "Product.BarcodeTooLong",
                $"A barcode cannot exceed {ProductBarcode.MaximumLength} characters."));
        }

        if (_barcodes.Exists(b => string.Equals(b.Barcode, scanned, StringComparison.Ordinal)))
        {
            return Result.Failure<ProductBarcode>(Error.Conflict(
                "Product.DuplicateBarcode",
                $"'{Description}' already carries the barcode '{scanned}'."));
        }

        ProductBarcode added = new(
            ProductBarcodeId.NewId(), TenantId, Id, scanned, rates ?? Rates);

        _barcodes.Add(added);

        return Result.Success(added);
    }

    /// <summary>Removes a barcode.</summary>
    /// <param name="barcodeId">The barcode to remove.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveBarcode(ProductBarcodeId barcodeId) =>
        _barcodes.RemoveAll(b => b.Id == barcodeId) == 0
            ? Result.Failure(Error.NotFound(
                "Product.BarcodeNotFound", "That barcode does not belong to this product."))
            : Result.Success();

    /// <summary>Withdraws the product from the range while leaving stock sellable.</summary>
    /// <remarks>
    /// Distinct from deactivating. A discontinued product is kept off new orders and
    /// still sold down from stock, which is what a range change actually looks like.
    /// </remarks>
    public void Discontinue() => IsDiscontinued = true;

    /// <summary>Returns the product to the range.</summary>
    public void Reinstate() => IsDiscontinued = false;

    /// <summary>Stops the product being transacted at all.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the product to be transacted again.</summary>
    public void Activate() => IsActive = true;

    /// <summary>Confirms the product may be put on a document.</summary>
    /// <returns>Success, or the reason it may not.</returns>
    /// <remarks>
    /// The single check every transaction makes, so the rule lives in one place rather
    /// than being re-derived by each screen that needs it - which is how a product
    /// ends up sellable on one and not on another.
    /// </remarks>
    public Result EnsureTransactable()
    {
        if (IsDeleted)
        {
            return Result.Failure(Error.Validation(
                "Product.Deleted", $"'{Description}' has been deleted."));
        }

        return IsActive
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "Product.Inactive",
                $"'{Description}' is inactive and cannot be put on a document."));
    }

    private static Result ValidateIdentity(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "Product.CodeRequired", "A product code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure(Error.Validation(
                "Product.CodeTooLong",
                $"A product code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(Error.Validation(
                "Product.DescriptionRequired", "A product description is required."));
        }

        return description.Trim().Length > MaximumDescriptionLength
            ? Result.Failure(Error.Validation(
                "Product.DescriptionTooLong",
                $"A product description cannot exceed {MaximumDescriptionLength} characters."))
            : Result.Success();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result Apply(Action change)
    {
        change();

        return Result.Success();
    }
}

/// <summary>One barcode a product may be found by, with the rates it prices at.</summary>
/// <remarks>
/// The specification's multiple-rate barcode grid. A product genuinely carries several
/// - the manufacturer's EAN, a shelf label printed in store, a supplier's own code -
/// and the grid gives each its own rates because a multipack scanned at the till is
/// the same product at a different price.
/// </remarks>
public sealed class ProductBarcode : Entity<ProductBarcodeId>, ITenantScoped
{
    /// <summary>The longest a barcode may be.</summary>
    public const int MaximumLength = 60;

    /// <summary>Initialises a new instance of the <see cref="ProductBarcode"/> class.</summary>
    /// <param name="id">The barcode's identity.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="productId">The product it belongs to.</param>
    /// <param name="barcode">The barcode as scanned.</param>
    /// <param name="rates">The rates it prices at.</param>
    internal ProductBarcode(
        ProductBarcodeId id,
        TenantId tenantId,
        ProductId productId,
        string barcode,
        ProductRates rates)
        : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        Barcode = barcode;
        Rates = rates;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private ProductBarcode()
    {
        Barcode = string.Empty;
        Rates = ProductRates.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the product it belongs to.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the barcode as scanned.</summary>
    public string Barcode { get; private set; }

    /// <summary>Gets the rates this barcode prices at.</summary>
    public ProductRates Rates { get; private set; }

    /// <summary>Replaces the rates this barcode prices at.</summary>
    /// <param name="rates">The new rates.</param>
    public void SetRates(ProductRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        Rates = rates;
    }
}
