using ERP.Application.Inventory.Products;
using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes product photographs.</summary>
public sealed class ProductPhotoRepository : IProductPhotoRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="ProductPhotoRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ProductPhotoRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductPhoto>> ListAsync(
        ProductId productId,
        CancellationToken cancellationToken = default) =>
        await _context.ProductPhotos
            .Where(photo => photo.ProductId == productId)
            .OrderBy(photo => photo.Slot)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(ProductPhoto photo) => _context.ProductPhotos.Add(photo);

    /// <inheritdoc />
    public void Remove(ProductPhoto photo) => _context.ProductPhotos.Remove(photo);
}
