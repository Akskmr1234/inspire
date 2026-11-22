using ERP.Application.Inventory.Products;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
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

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> ListThumbnailsAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default)
    {
        // Only the thumbnails, never the full pictures: a catalog of hundreds of
        // products asks for this at once.
        var rows = await _context.ProductPhotos.AsNoTracking()
            .Where(photo => photo.FirmId == firmId && photo.Thumbnail != null)
            .OrderBy(photo => photo.Slot)
            .Select(photo => new { photo.ProductId, photo.Thumbnail })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> thumbnails = [];

        foreach (var row in rows)
        {
            thumbnails.TryAdd(row.ProductId.Value, row.Thumbnail!);
        }

        return thumbnails;
    }
}
