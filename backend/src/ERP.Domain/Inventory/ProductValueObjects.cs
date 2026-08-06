using ERP.SharedKernel.Results;

namespace ERP.Domain.Inventory;

/// <summary>How a product's cost is arrived at.</summary>
/// <remarks>
/// The prose names these two and no others. FIFO is absent from it yet implied by
/// batch-wise costing, and is recorded as open question 6 rather than guessed at -
/// adding it here would commit the valuation report to a method nobody has asked for.
/// </remarks>
public enum CostingMethod
{
    /// <summary>The rate on the most recent purchase.</summary>
    LastPurchaseRate = 1,

    /// <summary>The weighted average of everything on hand.</summary>
    AverageRate = 2,
}

/// <summary>What kind of thing a product is.</summary>
public enum ItemType
{
    /// <summary>Held in stock, counted, and valued.</summary>
    Stock = 1,

    /// <summary>Sold but never held - labour, a repair charge, a delivery fee.</summary>
    Service = 2,

    /// <summary>
    /// Bought and sold without being tracked: consumables written off on receipt.
    /// </summary>
    NonStock = 3,
}

/// <summary>How quickly a product turns over.</summary>
/// <remarks>
/// Set by hand on the master rather than derived. The reference application shows it
/// as a field an operator picks, and a figure derived from movement would need a
/// window, a threshold, and a schedule that nobody has specified - all three of which
/// would then be wrong for somebody.
/// </remarks>
public enum MovementClass
{
    /// <summary>Not yet classified.</summary>
    Unclassified = 0,

    /// <summary>Turns over quickly.</summary>
    Fast = 1,

    /// <summary>Turns over at an ordinary rate.</summary>
    Normal = 2,

    /// <summary>Turns over slowly.</summary>
    Slow = 3,

    /// <summary>Has not moved and is not expected to.</summary>
    Dead = 4,
}

/// <summary>
/// The rate block: what a product costs and what it sells for.
/// </summary>
/// <param name="Cost">The cost, in the product's currency.</param>
/// <param name="ProfitPercentage">The margin the sales rates are built on.</param>
/// <param name="CorPercentage">
/// The reference application's <c>COR%</c>. Carried because the screen has it and a
/// migration would lose it; its meaning is undefined in the source document and is
/// recorded as open question 5. Nothing computes from it until somebody says what it
/// is.
/// </param>
/// <param name="RetailRate">The rate to a walk-in customer.</param>
/// <param name="WholesaleRate">The rate to a trade customer.</param>
/// <param name="OtherRate">A third rate the reference application exposes.</param>
/// <param name="MaximumRetailPrice">
/// The printed MRP. A ceiling rather than a rate charged - Indian retail requires it
/// on the pack, and selling above it is an offence.
/// </param>
/// <remarks>
/// Held together because they are set together and read together, and because the
/// only rules worth enforcing span more than one of them. They are decimals rather
/// than <c>Money</c>: the currency is the product's, held once on
/// <see cref="Product.Currency"/>, and repeating it seven times would create seven
/// chances for a product to be internally inconsistent about what it is priced in.
/// <see cref="Product"/> exposes each as <c>Money</c> at its boundary, so nothing
/// outside sees a bare decimal.
/// </remarks>
public sealed record ProductRates(
    decimal Cost,
    decimal ProfitPercentage,
    decimal CorPercentage,
    decimal RetailRate,
    decimal WholesaleRate,
    decimal OtherRate,
    decimal MaximumRetailPrice)
{
    /// <summary>Gets a rate block with everything at zero.</summary>
    /// <remarks>
    /// The state a product is created in. A product may legitimately be set up before
    /// anybody knows what it costs - that is what the first purchase is for - and
    /// refusing to create one until every rate is known would stop the purchase being
    /// entered at all.
    /// </remarks>
    public static ProductRates Empty { get; } = new(0m, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>Builds a validated rate block.</summary>
    /// <param name="cost">The cost.</param>
    /// <param name="profitPercentage">The margin the sales rates are built on.</param>
    /// <param name="corPercentage">The reference application's COR%.</param>
    /// <param name="retailRate">The retail rate.</param>
    /// <param name="wholesaleRate">The wholesale rate.</param>
    /// <param name="otherRate">The third rate.</param>
    /// <param name="maximumRetailPrice">The printed MRP.</param>
    /// <returns>The rate block, or a validation failure.</returns>
    /// <remarks>
    /// Negative rates are refused; nothing else is. A retail rate below cost is a
    /// loss-leader, not a mistake, and a system that refused one would be wrong about
    /// a decision that is the operator's to make. Where the MRP is stated, the retail
    /// rate may not exceed it - that one is not a preference but the law the MRP
    /// exists to express.
    /// </remarks>
    public static Result<ProductRates> Create(
        decimal cost,
        decimal profitPercentage,
        decimal corPercentage,
        decimal retailRate,
        decimal wholesaleRate,
        decimal otherRate,
        decimal maximumRetailPrice)
    {
        (string Name, decimal Value)[] figures =
        [
            ("cost", cost),
            ("profit percentage", profitPercentage),
            ("COR percentage", corPercentage),
            ("retail rate", retailRate),
            ("wholesale rate", wholesaleRate),
            ("other rate", otherRate),
            ("maximum retail price", maximumRetailPrice),
        ];

        foreach ((string name, decimal value) in figures)
        {
            if (value < 0m)
            {
                return Result.Failure<ProductRates>(Error.Validation(
                    "Product.NegativeRate", $"A product's {name} cannot be negative."));
            }
        }

        // Only checked when an MRP is actually stated. Zero means "not applicable",
        // which is the ordinary case outside Indian retail, and treating it as a
        // ceiling would forbid every non-zero rate.
        if (maximumRetailPrice > 0m && retailRate > maximumRetailPrice)
        {
            return Result.Failure<ProductRates>(Error.Validation(
                "Product.RetailAboveMrp",
                $"A retail rate of {retailRate} exceeds the maximum retail price of " +
                $"{maximumRetailPrice}. The MRP is a printed ceiling, not a suggestion."));
        }

        return Result.Success(new ProductRates(
            cost, profitPercentage, corPercentage, retailRate, wholesaleRate,
            otherRate, maximumRetailPrice));
    }
}

/// <summary>
/// The reorder thresholds a stock report reads to decide what needs buying.
/// </summary>
/// <param name="Minimum">The level below which stock is critical.</param>
/// <param name="Reorder">The level at which a purchase should be raised.</param>
/// <param name="Maximum">The level above which stock is overstocked.</param>
/// <remarks>
/// One value object rather than three fields because the only rules that matter are
/// between them: minimum at or below reorder, reorder at or below maximum. Held apart,
/// nothing would own that ordering and a product could sit with a reorder level below
/// its own minimum - which reads as "already critical, do not order", the exact
/// opposite of what it means.
/// </remarks>
public sealed record StockLevels(decimal Minimum, decimal Reorder, decimal Maximum)
{
    /// <summary>Gets levels with nothing set, which is no reorder policy at all.</summary>
    public static StockLevels None { get; } = new(0m, 0m, 0m);

    /// <summary>Builds a validated set of levels.</summary>
    /// <param name="minimum">The critical level.</param>
    /// <param name="reorder">The level at which to raise a purchase.</param>
    /// <param name="maximum">The overstocked level. Zero means no ceiling.</param>
    /// <returns>The levels, or a validation failure.</returns>
    public static Result<StockLevels> Create(decimal minimum, decimal reorder, decimal maximum)
    {
        if (minimum < 0m || reorder < 0m || maximum < 0m)
        {
            return Result.Failure<StockLevels>(Error.Validation(
                "Product.NegativeStockLevel", "A stock level cannot be negative."));
        }

        if (minimum > reorder)
        {
            return Result.Failure<StockLevels>(Error.Validation(
                "Product.MinimumAboveReorder",
                $"A minimum level of {minimum} sits above the reorder level of {reorder}, " +
                $"which would read as 'already critical, do not order'."));
        }

        // Zero is "no ceiling" rather than a ceiling of nothing. Without that, every
        // product left with a maximum of zero would refuse any reorder level at all.
        return maximum > 0m && reorder > maximum
            ? Result.Failure<StockLevels>(Error.Validation(
                "Product.ReorderAboveMaximum",
                $"A reorder level of {reorder} sits above the maximum of {maximum}, so " +
                $"acting on it would immediately overstock."))
            : Result.Success(new StockLevels(minimum, reorder, maximum));
    }
}

/// <summary>
/// The mobile-device attributes the reference application puts on the product master.
/// </summary>
/// <param name="Device">The device model.</param>
/// <param name="Colour">The colour.</param>
/// <param name="Battery">The battery capacity, as printed.</param>
/// <param name="Ram">The memory, as printed.</param>
/// <param name="Storage">The storage, as printed.</param>
/// <remarks>
/// <para>
/// Free text rather than typed quantities, deliberately. These are printed on a box
/// and searched for as they appear there - "128GB", "5000mAh" - and parsing them into
/// numbers would mean choosing units, handling the ones that do not fit, and
/// presenting them back differently from how the box reads.
/// </para>
/// <para>
/// On every product rather than only on devices, because the service module works by
/// mobile number and device attributes, and a product that could not carry them would
/// be invisible to it. A grocery item simply leaves them empty.
/// </para>
/// </remarks>
public sealed record DeviceAttributes(
    string? Device,
    string? Colour,
    string? Battery,
    string? Ram,
    string? Storage)
{
    /// <summary>The longest any one device attribute may be.</summary>
    public const int MaximumLength = 60;

    /// <summary>Gets attributes with nothing set.</summary>
    public static DeviceAttributes None { get; } = new(null, null, null, null, null);

    /// <summary>Builds a validated, trimmed set of attributes.</summary>
    /// <param name="device">The device model.</param>
    /// <param name="colour">The colour.</param>
    /// <param name="battery">The battery capacity.</param>
    /// <param name="ram">The memory.</param>
    /// <param name="storage">The storage.</param>
    /// <returns>The attributes, or a validation failure.</returns>
    public static Result<DeviceAttributes> Create(
        string? device,
        string? colour,
        string? battery,
        string? ram,
        string? storage)
    {
        (string Name, string? Value)[] attributes =
        [
            ("device", device),
            ("colour", colour),
            ("battery", battery),
            ("RAM", ram),
            ("storage", storage),
        ];

        foreach ((string name, string? value) in attributes)
        {
            if (value?.Trim().Length > MaximumLength)
            {
                return Result.Failure<DeviceAttributes>(Error.Validation(
                    "Product.DeviceAttributeTooLong",
                    $"A product's {name} cannot exceed {MaximumLength} characters."));
            }
        }

        return Result.Success(new DeviceAttributes(
            Clean(device), Clean(colour), Clean(battery), Clean(ram), Clean(storage)));
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
