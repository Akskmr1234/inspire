using ERP.Application.Inventory.Masters;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes tax rates.</summary>
public sealed class TaxSlabRepository : ITaxSlabRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TaxSlabRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TaxSlabRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxSlab>> ListAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await _context.TaxSlabs
            .AsNoTracking()
            .Where(slab => slab.FirmId == firmId && (includeInactive || slab.IsActive))
            .OrderBy(slab => slab.Percentage)
            .ThenBy(slab => slab.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TaxSlab?> FindAsync(TaxSlabId id, CancellationToken cancellationToken = default) =>
        _context.TaxSlabs.FirstOrDefaultAsync(slab => slab.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<TaxSlab?> FindDefaultAsync(FirmId firmId, CancellationToken cancellationToken = default) =>
        _context.TaxSlabs.FirstOrDefaultAsync(
            slab => slab.FirmId == firmId && slab.IsDefault, cancellationToken);

    /// <inheritdoc />
    public void Add(TaxSlab slab) => _context.TaxSlabs.Add(slab);
}
