using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>
/// A unit something is counted, weighed, or measured in.
/// </summary>
/// <remarks>
/// <para>
/// Units form flat groups rather than a hierarchy. A base unit - <c>No</c>, <c>Kg</c>,
/// <c>Litre</c> - stands for itself, and every other unit in its group converts
/// directly to it: a <c>Pack</c> of twelve, a <c>Box</c> of twenty-four. The
/// specification's own example is exactly this shape, and it is what
/// <see cref="GroupId"/> identifies.
/// </para>
/// <para>
/// Deliberately flat, and the depth limit is the point. Allowing a Box to be defined
/// as two Packs, each of twelve, means every conversion compounds - and with a factor
/// that is not a whole number, compounding is where the rounding error comes from.
/// Twelve boxes of a 0.33-litre bottle should be the same quantity however the chain
/// is walked, and only a single hop guarantees that.
/// </para>
/// <para>
/// Scoped to the firm rather than the tenant. Two companies under one group may well
/// buy in different pack sizes from different suppliers, and a shared unit list would
/// make one of them wrong.
/// </para>
/// </remarks>
public sealed class UnitOfMeasure : AggregateRoot<UnitOfMeasureId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a unit code may be.</summary>
    public const int MaximumCodeLength = 20;

    /// <summary>The longest a unit name may be.</summary>
    public const int MaximumNameLength = 60;

    /// <summary>The longest a symbol may be.</summary>
    public const int MaximumSymbolLength = 10;

    /// <summary>The most decimal places a quantity may carry.</summary>
    /// <remarks>
    /// Six, matching the scale quantities are stored at. Beyond that the extra digits
    /// would be dropped on save, and a unit promising them would be lying.
    /// </remarks>
    public const int MaximumDecimalPlaces = 6;

    private UnitOfMeasure(
        UnitOfMeasureId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        string? symbol,
        UnitOfMeasureId? baseUnitId,
        decimal conversionFactor,
        int decimalPlaces)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        Symbol = symbol;
        BaseUnitId = baseUnitId;
        ConversionFactor = conversionFactor;
        DecimalPlaces = decimalPlaces;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private UnitOfMeasure()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the unit's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the short form printed on a document, such as <c>kg</c>.</summary>
    public string? Symbol { get; private set; }

    /// <summary>Gets the base unit this one converts to, or null when it is a base.</summary>
    public UnitOfMeasureId? BaseUnitId { get; private set; }

    /// <summary>Gets how many base units one of this unit is worth.</summary>
    /// <remarks>
    /// One for a base unit, by definition. Twelve for a pack of twelve. Fractional
    /// factors are allowed - a unit may legitimately be a half or a third of its base -
    /// but never zero or negative, either of which would make a conversion meaningless
    /// or reverse it.
    /// </remarks>
    public decimal ConversionFactor { get; private set; }

    /// <summary>Gets how many decimal places a quantity in this unit may carry.</summary>
    /// <remarks>
    /// Zero for anything counted rather than measured. Half a bottle is a quantity;
    /// half a serial-numbered handset is a data-entry error, and a unit that says so
    /// catches it at the point of entry rather than at stock-take.
    /// </remarks>
    public int DecimalPlaces { get; private set; }

    /// <summary>Gets whether the unit may still be used on new documents.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether this unit is the base of its group.</summary>
    public bool IsBaseUnit => BaseUnitId is null;

    /// <summary>
    /// Gets the measurement group this unit belongs to, identified by its base unit.
    /// </summary>
    /// <remarks>
    /// A base unit is its own group. This is what makes "only units in the product's
    /// measurement group may be selected" a single comparison rather than a walk.
    /// </remarks>
    public UnitOfMeasureId GroupId => BaseUnitId ?? Id;

    /// <summary>Creates a base unit, which everything in its group converts to.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The unit's name.</param>
    /// <param name="symbol">The short form printed on documents.</param>
    /// <param name="decimalPlaces">How many decimals a quantity may carry.</param>
    /// <returns>The unit, or a validation failure.</returns>
    public static Result<UnitOfMeasure> CreateBase(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        string? symbol = null,
        int decimalPlaces = 0)
    {
        Result validation = Validate(code, name, symbol, decimalPlaces);

        return validation.IsFailure
            ? Result.Failure<UnitOfMeasure>(validation.Error)
            : Result.Success(new UnitOfMeasure(
                UnitOfMeasureId.NewId(), tenantId, firmId,
                code.Trim().ToUpperInvariant(), name.Trim(), Clean(symbol),
                baseUnitId: null, conversionFactor: 1m, decimalPlaces));
    }

    /// <summary>Creates a unit that converts to an existing base.</summary>
    /// <param name="baseUnit">The base unit of the group.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The unit's name.</param>
    /// <param name="conversionFactor">How many base units one of this is worth.</param>
    /// <param name="symbol">The short form printed on documents.</param>
    /// <param name="decimalPlaces">How many decimals a quantity may carry.</param>
    /// <returns>The unit, or a validation failure.</returns>
    /// <remarks>
    /// The parent must itself be a base unit. Refusing a chain here is what keeps
    /// every conversion a single multiplication, and therefore free of the compounding
    /// error a chain would introduce.
    /// </remarks>
    public static Result<UnitOfMeasure> CreateDerived(
        UnitOfMeasure baseUnit,
        string code,
        string name,
        decimal conversionFactor,
        string? symbol = null,
        int decimalPlaces = 0)
    {
        ArgumentNullException.ThrowIfNull(baseUnit);

        if (!baseUnit.IsBaseUnit)
        {
            return Result.Failure<UnitOfMeasure>(Error.Validation(
                "UnitOfMeasure.NotABaseUnit",
                $"'{baseUnit.Name}' is itself derived. A unit must convert directly to "
                + $"a base unit, so that no conversion compounds."));
        }

        if (conversionFactor <= 0m)
        {
            return Result.Failure<UnitOfMeasure>(Error.Validation(
                "UnitOfMeasure.FactorNotPositive",
                $"A conversion factor must be greater than zero, but {conversionFactor} "
                + $"was supplied."));
        }

        Result validation = Validate(code, name, symbol, decimalPlaces);

        return validation.IsFailure
            ? Result.Failure<UnitOfMeasure>(validation.Error)
            : Result.Success(new UnitOfMeasure(
                UnitOfMeasureId.NewId(), baseUnit.TenantId, baseUnit.FirmId,
                code.Trim().ToUpperInvariant(), name.Trim(), Clean(symbol),
                baseUnit.Id, conversionFactor, decimalPlaces));
    }

    /// <summary>Determines whether a quantity may be converted between two units.</summary>
    /// <param name="other">The unit being converted to.</param>
    /// <returns><see langword="true"/> when both belong to the same group.</returns>
    /// <remarks>
    /// The specification's rule, stated once: a product measured in <c>No</c> may be
    /// entered in <c>Pack</c> or <c>Box</c>, and never in <c>Litre</c>. Nothing
    /// relates a count to a volume, and a system that guessed at one would be inventing
    /// stock.
    /// </remarks>
    public bool IsInSameGroupAs(UnitOfMeasure other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return GroupId == other.GroupId;
    }

    /// <summary>Converts a quantity in this unit into base units.</summary>
    /// <param name="quantity">The quantity as entered.</param>
    /// <returns>The quantity in base units.</returns>
    public decimal ToBase(decimal quantity) => quantity * ConversionFactor;

    /// <summary>Converts a quantity in base units into this unit.</summary>
    /// <param name="baseQuantity">The quantity in base units.</param>
    /// <returns>The quantity in this unit.</returns>
    public decimal FromBase(decimal baseQuantity) => baseQuantity / ConversionFactor;

    /// <summary>Converts a quantity between two units of the same group.</summary>
    /// <param name="quantity">The quantity as entered.</param>
    /// <param name="from">The unit it is entered in.</param>
    /// <param name="to">The unit it is wanted in.</param>
    /// <returns>The converted quantity, or the reason it could not be converted.</returns>
    /// <remarks>
    /// Through the base rather than by a direct ratio, so a group of any size needs
    /// only one factor per unit rather than a factor for every pair of them.
    /// </remarks>
    public static Result<decimal> Convert(
        decimal quantity,
        UnitOfMeasure from,
        UnitOfMeasure to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return from.IsInSameGroupAs(to)
            ? Result.Success(to.FromBase(from.ToBase(quantity)))
            : Result.Failure<decimal>(Error.Validation(
                "UnitOfMeasure.DifferentGroups",
                $"'{from.Name}' and '{to.Name}' measure different things and cannot be "
                + $"converted between."));
    }

    /// <summary>Checks that a quantity carries no more precision than the unit allows.</summary>
    /// <param name="quantity">The quantity as entered.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result EnsurePrecision(decimal quantity)
    {
        // A round trip rather than an inspection of the scale. Decimal keeps trailing
        // zeros - 1.10 has a scale of two - so a scale test would reject a quantity
        // that is exactly representable. Rounding and comparing asks the question that
        // actually matters: does anything get lost.
        return decimal.Round(quantity, DecimalPlaces, MidpointRounding.AwayFromZero)
            != quantity
            ? Result.Failure(Error.Validation(
                "UnitOfMeasure.TooPrecise",
                $"'{Name}' is measured to {DecimalPlaces} decimal places, so {quantity} "
                + $"cannot be entered."))
            : Result.Success();
    }

    /// <summary>Renames the unit.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.NameRequired", "A unit name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Stops the unit being used on new documents.</summary>
    /// <remarks>
    /// Deactivated rather than deleted. Documents already entered in it must go on
    /// meaning what they meant, and a unit that vanished would leave their quantities
    /// unreadable.
    /// </remarks>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the unit to be used again.</summary>
    public void Activate() => IsActive = true;

    private static string? Clean(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();

    private static Result Validate(
        string code,
        string name,
        string? symbol,
        int decimalPlaces)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.CodeRequired", "A unit code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.CodeTooLong",
                $"A unit code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.NameRequired", "A unit name is required."));
        }

        if (name.Trim().Length > MaximumNameLength)
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.NameTooLong",
                $"A unit name cannot exceed {MaximumNameLength} characters."));
        }

        if (symbol is not null && symbol.Trim().Length > MaximumSymbolLength)
        {
            return Result.Failure(Error.Validation(
                "UnitOfMeasure.SymbolTooLong",
                $"A symbol cannot exceed {MaximumSymbolLength} characters."));
        }

        return decimalPlaces is < 0 or > MaximumDecimalPlaces
            ? Result.Failure(Error.Validation(
                "UnitOfMeasure.DecimalPlacesOutOfRange",
                $"Decimal places must be between 0 and {MaximumDecimalPlaces}."))
            : Result.Success();
    }
}
