using ERP.Application.Abstractions.Persistence;
using ERP.Application.Inventory.Masters;
using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes the inventory masters.</summary>
public sealed class InventoryMasterRepository : IInventoryMasterRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="InventoryMasterRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public InventoryMasterRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure>> GetUnitsAsync(
        IReadOnlyCollection<UnitOfMeasureId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<UnitOfMeasureId, UnitOfMeasure>();
        }

        List<UnitOfMeasureId> wanted = [.. ids.Distinct()];

        return await _context.UnitsOfMeasure
            .Where(unit => wanted.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<UnitOfMeasure?> FindUnitAsync(
        UnitOfMeasureId id,
        CancellationToken cancellationToken = default) =>
        _context.UnitsOfMeasure.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Category?> FindCategoryAsync(
        CategoryId id,
        CancellationToken cancellationToken = default) =>
        _context.Categories.FirstOrDefaultAsync(
            category => category.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Brand?> FindBrandAsync(
        BrandId id,
        CancellationToken cancellationToken = default) =>
        _context.Brands.FirstOrDefaultAsync(brand => brand.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Warehouse?> FindWarehouseAsync(
        WarehouseId id,
        CancellationToken cancellationToken = default) =>
        _context.Warehouses.FirstOrDefaultAsync(
            warehouse => warehouse.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Warehouse?> FindDefaultWarehouseAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        _context.Warehouses.FirstOrDefaultAsync(
            warehouse => warehouse.FirmId == firmId && warehouse.IsDefault,
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        InventoryMasterKind kind,
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) => kind switch
        {
            InventoryMasterKind.UnitOfMeasure => _context.UnitsOfMeasure.AnyAsync(
                unit => unit.FirmId == firmId && unit.Code == code, cancellationToken),
            InventoryMasterKind.Category => _context.Categories.AnyAsync(
                category => category.FirmId == firmId && category.Code == code,
                cancellationToken),
            InventoryMasterKind.Brand => _context.Brands.AnyAsync(
                brand => brand.FirmId == firmId && brand.Code == code, cancellationToken),
            InventoryMasterKind.Warehouse => _context.Warehouses.AnyAsync(
                warehouse => warehouse.FirmId == firmId && warehouse.Code == code,
                cancellationToken),
            _ => Task.FromResult(false),
        };

    /// <inheritdoc />
    public void Add(UnitOfMeasure unit) => _context.UnitsOfMeasure.Add(unit);

    /// <inheritdoc />
    public void Add(Category category) => _context.Categories.Add(category);

    /// <inheritdoc />
    public void Add(Brand brand) => _context.Brands.Add(brand);

    /// <inheritdoc />
    public void Add(Warehouse warehouse) => _context.Warehouses.Add(warehouse);
}

/// <summary>Reads the inventory masters for a firm.</summary>
/// <remarks>
/// Projections rather than aggregates. A master list is read far more often than it is
/// written, and loading four aggregates to show four columns of each would be work
/// nobody asked for.
/// </remarks>
public sealed class InventoryMasterReader : IInventoryMasterReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="InventoryMasterReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public InventoryMasterReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitSummary>> ReadUnitsAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        // Left-joined to the base unit, because a base unit has none and an inner join
        // would return only the derived ones - which is every unit except the ones the
        // groups are built on.
        var units = await _context.UnitsOfMeasure
            .Where(unit => unit.FirmId == firmId && (includeInactive || unit.IsActive))
            .GroupJoin(
                _context.UnitsOfMeasure,
                unit => unit.BaseUnitId,
                candidate => (UnitOfMeasureId?)candidate.Id,
                (unit, bases) => new { unit, bases })
            .SelectMany(
                pair => pair.bases.DefaultIfEmpty(),
                (pair, baseUnit) => new
                {
                    pair.unit.Id,
                    pair.unit.Code,
                    pair.unit.Name,
                    pair.unit.Symbol,
                    pair.unit.BaseUnitId,
                    BaseUnitCode = baseUnit == null ? null : baseUnit.Code,
                    pair.unit.ConversionFactor,
                    pair.unit.DecimalPlaces,
                    pair.unit.IsActive,
                })
            // Base units first, then their derivatives: a group reads top-down the way
            // somebody thinks about it.
            .OrderBy(row => row.BaseUnitId == null ? 0 : 1)
            .ThenBy(row => row.Code)
            .ToListAsync(cancellationToken);

        return
        [
            .. units.Select(row => new UnitSummary(
                row.Id.Value,
                row.Code,
                row.Name,
                row.Symbol,
                row.BaseUnitId?.Value,
                row.BaseUnitCode,
                row.ConversionFactor,
                row.DecimalPlaces,
                row.IsActive)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategorySummary>> ReadCategoriesAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var categories = await _context.Categories
            .Where(category =>
                category.FirmId == firmId && (includeInactive || category.IsActive))
            .GroupJoin(
                _context.Categories,
                category => category.ParentId,
                candidate => (CategoryId?)candidate.Id,
                (category, parents) => new { category, parents })
            .SelectMany(
                pair => pair.parents.DefaultIfEmpty(),
                (pair, parent) => new
                {
                    pair.category.Id,
                    pair.category.ParentId,
                    ParentName = parent == null ? null : parent.Name,
                    pair.category.Code,
                    pair.category.Name,
                    pair.category.NameArabic,
                    pair.category.IsActive,
                })
            .OrderBy(row => row.Code)
            .ToListAsync(cancellationToken);

        return
        [
            .. categories.Select(row => new CategorySummary(
                row.Id.Value,
                row.ParentId?.Value,
                row.ParentName,
                row.Code,
                row.Name,
                row.NameArabic,
                row.IsActive)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrandSummary>> ReadBrandsAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var brands = await _context.Brands
            .Where(brand => brand.FirmId == firmId && (includeInactive || brand.IsActive))
            .OrderBy(brand => brand.Code)
            .Select(brand => new
            {
                brand.Id,
                brand.Code,
                brand.Name,
                brand.NameArabic,
                brand.IsActive,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. brands.Select(row => new BrandSummary(
                row.Id.Value, row.Code, row.Name, row.NameArabic, row.IsActive)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseSummary>> ReadWarehousesAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var warehouses = await _context.Warehouses
            .Where(warehouse =>
                warehouse.FirmId == firmId && (includeInactive || warehouse.IsActive))
            .GroupJoin(
                _context.Branches,
                warehouse => warehouse.BranchId,
                branch => (BranchId?)branch.Id,
                (warehouse, branches) => new { warehouse, branches })
            .SelectMany(
                pair => pair.branches.DefaultIfEmpty(),
                (pair, branch) => new
                {
                    pair.warehouse.Id,
                    pair.warehouse.Code,
                    pair.warehouse.Name,
                    pair.warehouse.NameArabic,
                    pair.warehouse.BranchId,
                    BranchName = branch == null ? null : branch.Name,
                    pair.warehouse.Address,
                    pair.warehouse.IsDefault,
                    pair.warehouse.IsActive,
                })
            // The default leads, being the one most rows will be entered against.
            .OrderByDescending(row => row.IsDefault)
            .ThenBy(row => row.Code)
            .ToListAsync(cancellationToken);

        return
        [
            .. warehouses.Select(row => new WarehouseSummary(
                row.Id.Value,
                row.Code,
                row.Name,
                row.NameArabic,
                row.BranchId?.Value,
                row.BranchName,
                row.Address,
                row.IsDefault,
                row.IsActive)),
        ];
    }
}
