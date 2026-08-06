using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Masters;

// -------------------------------------------------------------------------- reading

/// <summary>Lists the units of measurement.</summary>
/// <param name="IncludeInactive">Whether to include units withdrawn from use.</param>
public sealed record ListUnitsQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<UnitSummary>>;

/// <summary>A unit as a list shows it.</summary>
/// <param name="Id">The unit.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Symbol">The short form printed on documents.</param>
/// <param name="BaseUnitId">The base it converts to, or null when it is a base.</param>
/// <param name="BaseUnitCode">The base's code, for display.</param>
/// <param name="ConversionFactor">How many base units one of this is worth.</param>
/// <param name="DecimalPlaces">How many decimals a quantity may carry.</param>
/// <param name="IsActive">Whether it may still be used.</param>
public sealed record UnitSummary(
    Guid Id,
    string Code,
    string Name,
    string? Symbol,
    Guid? BaseUnitId,
    string? BaseUnitCode,
    decimal ConversionFactor,
    int DecimalPlaces,
    bool IsActive);

/// <summary>Lists the product categories and sub-classes.</summary>
/// <param name="IncludeInactive">Whether to include categories withdrawn from use.</param>
public sealed record ListCategoriesQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<CategorySummary>>;

/// <summary>A category as a list shows it.</summary>
/// <param name="Id">The category.</param>
/// <param name="ParentId">The category it sits beneath, or null at the top level.</param>
/// <param name="ParentName">The parent's name, for display.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="IsActive">Whether it may still be assigned.</param>
public sealed record CategorySummary(
    Guid Id,
    Guid? ParentId,
    string? ParentName,
    string Code,
    string Name,
    string? NameArabic,
    bool IsActive);

/// <summary>Lists the brands.</summary>
/// <param name="IncludeInactive">Whether to include brands withdrawn from use.</param>
public sealed record ListBrandsQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<BrandSummary>>;

/// <summary>A brand as a list shows it.</summary>
/// <param name="Id">The brand.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="IsActive">Whether it may still be assigned.</param>
public sealed record BrandSummary(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    bool IsActive);

/// <summary>Lists the warehouses.</summary>
/// <param name="IncludeInactive">Whether to include warehouses withdrawn from use.</param>
public sealed record ListWarehousesQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<WarehouseSummary>>;

/// <summary>A warehouse as a list shows it.</summary>
/// <param name="Id">The warehouse.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="BranchId">The branch it belongs to, if any.</param>
/// <param name="BranchName">The branch's name, for display.</param>
/// <param name="Address">Where it is.</param>
/// <param name="IsDefault">Whether new documents default to it.</param>
/// <param name="IsActive">Whether stock may still move through it.</param>
public sealed record WarehouseSummary(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    Guid? BranchId,
    string? BranchName,
    string? Address,
    bool IsDefault,
    bool IsActive);

/// <summary>Reads the inventory masters for a firm.</summary>
public interface IInventoryMasterReader
{
    /// <summary>Reads the units of measurement.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether to include withdrawn units.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units, base units first, then by code.</returns>
    Task<IReadOnlyList<UnitSummary>> ReadUnitsAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the categories.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether to include withdrawn categories.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories, by code.</returns>
    Task<IReadOnlyList<CategorySummary>> ReadCategoriesAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the brands.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether to include withdrawn brands.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The brands, by code.</returns>
    Task<IReadOnlyList<BrandSummary>> ReadBrandsAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the warehouses.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether to include withdrawn warehouses.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The warehouses, the default first, then by code.</returns>
    Task<IReadOnlyList<WarehouseSummary>> ReadWarehousesAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------- writing

/// <summary>Adds a unit of measurement.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The unit's name.</param>
/// <param name="BaseUnitId">
/// The base it converts to, or null to create a base unit of a new group.
/// </param>
/// <param name="ConversionFactor">How many base units one of this is worth.</param>
/// <param name="Symbol">The short form printed on documents.</param>
/// <param name="DecimalPlaces">How many decimals a quantity may carry.</param>
public sealed record CreateUnitCommand(
    string Code,
    string Name,
    Guid? BaseUnitId = null,
    decimal ConversionFactor = 1m,
    string? Symbol = null,
    int DecimalPlaces = 0) : ICommand<Guid>;

/// <summary>Renames a unit of measurement.</summary>
/// <param name="UnitId">The unit.</param>
/// <param name="Name">The new name.</param>
public sealed record RenameUnitCommand(Guid UnitId, string Name) : ICommand;

/// <summary>Adds a category, optionally beneath an existing one.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The category name.</param>
/// <param name="ParentId">The category it sits beneath, or null for the top level.</param>
/// <param name="NameArabic">The name in Arabic.</param>
public sealed record CreateCategoryCommand(
    string Code,
    string Name,
    Guid? ParentId = null,
    string? NameArabic = null) : ICommand<Guid>;

/// <summary>Adds a brand.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The brand name.</param>
/// <param name="NameArabic">The name in Arabic.</param>
public sealed record CreateBrandCommand(
    string Code,
    string Name,
    string? NameArabic = null) : ICommand<Guid>;

/// <summary>Adds a warehouse.</summary>
/// <param name="Code">The code, unique within the firm.</param>
/// <param name="Name">The warehouse name.</param>
/// <param name="BranchId">The branch it belongs to, or null for a central store.</param>
/// <param name="NameArabic">The name in Arabic.</param>
/// <param name="Address">Where it is.</param>
public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    Guid? BranchId = null,
    string? NameArabic = null,
    string? Address = null) : ICommand<Guid>;

/// <summary>Makes one warehouse the one new documents default to.</summary>
/// <param name="WarehouseId">The warehouse.</param>
public sealed record SetDefaultWarehouseCommand(Guid WarehouseId) : ICommand;

/// <summary>Withdraws an inventory master from use, or returns it.</summary>
/// <param name="Kind">Which master.</param>
/// <param name="Id">The record.</param>
/// <param name="IsActive">Whether it should be usable.</param>
/// <remarks>
/// One command for four masters, because withdrawing a record from use is the same
/// act whichever it is, and none of them is ever deleted - documents already
/// referring to one must go on meaning what they meant.
/// </remarks>
public sealed record SetMasterActiveCommand(
    InventoryMasterKind Kind,
    Guid Id,
    bool IsActive) : ICommand;

/// <summary>Validates a <see cref="CreateUnitCommand"/>.</summary>
public sealed class CreateUnitCommandValidator : AbstractValidator<CreateUnitCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateUnitCommandValidator"/> class.</summary>
    public CreateUnitCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(UnitOfMeasure.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(UnitOfMeasure.MaximumNameLength);
        RuleFor(c => c.DecimalPlaces)
            .InclusiveBetween(0, UnitOfMeasure.MaximumDecimalPlaces);

        // Only meaningful on a derived unit; a base unit's factor is one by definition
        // and is not taken from the request at all.
        RuleFor(c => c.ConversionFactor)
            .GreaterThan(0m)
            .When(c => c.BaseUnitId.HasValue);
    }
}

/// <summary>Validates a <see cref="CreateCategoryCommand"/>.</summary>
public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateCategoryCommandValidator"/> class.</summary>
    public CreateCategoryCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Category.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Category.MaximumNameLength);
    }
}

/// <summary>Validates a <see cref="CreateBrandCommand"/>.</summary>
public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateBrandCommandValidator"/> class.</summary>
    public CreateBrandCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Brand.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Brand.MaximumNameLength);
    }
}

/// <summary>Validates a <see cref="CreateWarehouseCommand"/>.</summary>
public sealed class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateWarehouseCommandValidator"/> class.</summary>
    public CreateWarehouseCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Warehouse.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Warehouse.MaximumNameLength);
        RuleFor(c => c.Address!).MaximumLength(Warehouse.MaximumAddressLength)
            .When(c => c.Address is not null);
    }
}

/// <summary>Shared setup for the inventory master handlers.</summary>
internal static class MasterContext
{
    /// <summary>Resolves the firm a master is being read or written in.</summary>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <returns>The firm, or the reason it could not be resolved.</returns>
    internal static Result<FirmId> ResolveFirm(ITenantContext tenantContext) =>
        tenantContext.FirmId is { } firmId
            ? Result.Success(firmId)
            : Result.Failure<FirmId>(Error.Forbidden(
                "InventoryMaster.NoFirmSelected",
                "A firm must be selected to work with inventory masters."));

    /// <summary>Refuses a code another record of the same kind already uses.</summary>
    /// <param name="masters">The master repository.</param>
    /// <param name="kind">Which master.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code being taken.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalised code, or the reason it was refused.</returns>
    internal static async Task<Result<string>> ReserveCodeAsync(
        IInventoryMasterRepository masters,
        InventoryMasterKind kind,
        FirmId firmId,
        string code,
        CancellationToken cancellationToken)
    {
        string normalised = code.Trim().ToUpperInvariant();

        return await masters.CodeExistsAsync(kind, firmId, normalised, cancellationToken)
            ? Result.Failure<string>(Error.Conflict(
                "InventoryMaster.CodeTaken",
                $"'{normalised}' is already used by another record of this kind."))
            : Result.Success(normalised);
    }
}
