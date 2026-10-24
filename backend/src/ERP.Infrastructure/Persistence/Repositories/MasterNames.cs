using System.Linq.Expressions;
using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Answers whether a master's name is taken, from the tables themselves.</summary>
public sealed class MasterNames : IMasterNames
{
    private const string Escape = @"\";

    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="MasterNames"/> class.</summary>
    /// <param name="context">The database context.</param>
    public MasterNames(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<bool> IsTakenAsync(
        NamedMaster master,
        FirmId firmId,
        string name,
        Guid? exceptId = null,
        Guid? scopeId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        string trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            return Task.FromResult(false);
        }

        // ILIKE rather than lower() on both sides: case-insensitive the way PostgreSQL
        // means it, with the name's own % and _ taken literally.
        string wanted = trimmed
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

        CountryId? scope = scopeId is { } country ? CountryId.From(country) : null;

        return master switch
        {
            NamedMaster.Product => Taken(
                _context.Products.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Description.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == ProductId.From(id) : null,
                cancellationToken),
            NamedMaster.Ledger => Taken(
                _context.Ledgers.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == LedgerId.From(id) : null,
                cancellationToken),
            NamedMaster.AccountGroup => Taken(
                _context.AccountGroups.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == AccountGroupId.From(id) : null,
                cancellationToken),
            NamedMaster.Category => Taken(
                _context.Categories.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == CategoryId.From(id) : null,
                cancellationToken),
            NamedMaster.Brand => Taken(
                _context.Brands.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == BrandId.From(id) : null,
                cancellationToken),
            NamedMaster.Unit => Taken(
                _context.UnitsOfMeasure.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == UnitOfMeasureId.From(id) : null,
                cancellationToken),
            NamedMaster.Warehouse => Taken(
                _context.Warehouses.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == WarehouseId.From(id) : null,
                cancellationToken),
            NamedMaster.TaxSlab => Taken(
                _context.TaxSlabs.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == TaxSlabId.From(id) : null,
                cancellationToken),
            NamedMaster.Country => Taken(
                _context.Countries.Where(row => row.FirmId == firmId && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == CountryId.From(id) : null,
                cancellationToken),
            NamedMaster.Region => Taken(
                _context.Regions.Where(row => row.FirmId == firmId
                    && (scope == null || row.CountryId == scope)
                    && EF.Functions.ILike(row.Name.Trim(), wanted, Escape)),
                exceptId is { } id ? row => row.Id == RegionId.From(id) : null,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(master), master, "Not a named master."),
        };
    }

    /// <summary>Whether a record other than the one being renamed carries the name.</summary>
    /// <typeparam name="TEntity">The master's entity.</typeparam>
    /// <param name="named">The rows with the name.</param>
    /// <param name="self">Picks out the record being renamed, where there is one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when another record has it.</returns>
    /// <remarks>
    /// A record that already has the name may keep it, even where another shares it:
    /// books brought over from another system can hold duplicates, and saving an
    /// unrelated change to one of them must not be refused for a clash nobody made here.
    /// </remarks>
    private static async Task<bool> Taken<TEntity>(
        IQueryable<TEntity> named,
        Expression<Func<TEntity, bool>>? self,
        CancellationToken cancellationToken)
    {
        if (self is not null && await named.AnyAsync(self, cancellationToken))
        {
            return false;
        }

        return await named.AnyAsync(cancellationToken);
    }
}
