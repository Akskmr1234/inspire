using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Parties;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Inventory.Products;

/// <summary>Reads every photograph a product has.</summary>
/// <param name="ProductId">The product.</param>
public sealed record ListProductPhotosQuery(Guid ProductId) : IQuery<PhotoSetResponse>;

/// <summary>Sets, replaces or removes one of a product's photographs.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Slot">Which photograph, from one; the first is its main one.</param>
/// <param name="Content">The picture as a data URL; null removes it.</param>
/// <param name="Thumbnail">A small copy of the picture, for the sales catalog.</param>
public sealed record SetProductPhotoCommand(Guid ProductId, int Slot, string? Content, string? Thumbnail = null)
    : ICommand;

/// <summary>Every product's thumbnail in the selected firm, for the sales catalog.</summary>
public sealed record ListProductThumbnailsQuery : IQuery<IReadOnlyDictionary<Guid, string>>;

/// <summary>Where product photographs are kept.</summary>
public interface IProductPhotoRepository
{
    /// <summary>Lists a product's photographs.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The photographs, in slot order.</returns>
    Task<IReadOnlyList<ProductPhoto>> ListAsync(
        ProductId productId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a photograph.</summary>
    /// <param name="photo">The photograph.</param>
    void Add(ProductPhoto photo);

    /// <summary>Removes a photograph.</summary>
    /// <param name="photo">The photograph.</param>
    void Remove(ProductPhoto photo);

    /// <summary>Each product's thumbnail: its first photograph that has one.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The thumbnails, by product.</returns>
    Task<IReadOnlyDictionary<Guid, string>> ListThumbnailsAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles product photographs.</summary>
public sealed class ProductPhotoHandlers
    : IQueryHandler<ListProductPhotosQuery, PhotoSetResponse>,
      IQueryHandler<ListProductThumbnailsQuery, IReadOnlyDictionary<Guid, string>>,
      ICommandHandler<SetProductPhotoCommand>
{
    private readonly IProductRepository _products;
    private readonly IProductPhotoRepository _photos;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ProductPhotoHandlers"/> class.</summary>
    /// <param name="products">The product master.</param>
    /// <param name="photos">Where the photographs are kept.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ProductPhotoHandlers(
        IProductRepository products,
        IProductPhotoRepository photos,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _photos = photos;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PhotoSetResponse>> Handle(
        ListProductPhotosQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> product = await FindAsync(request.ProductId, cancellationToken);

        if (product.IsFailure)
        {
            return Result.Failure<PhotoSetResponse>(product.Error);
        }

        IReadOnlyList<ProductPhoto> photos = await _photos.ListAsync(product.Value.Id, cancellationToken);

        return Result.Success(new PhotoSetResponse(
        [
            .. Enumerable.Range(1, ProductPhoto.MaximumSlots)
                .Select(slot => photos.FirstOrDefault(photo => photo.Slot == slot)?.Content),
        ]));
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetProductPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await FindAsync(request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Product product = found.Value;
        IReadOnlyList<ProductPhoto> photos = await _photos.ListAsync(product.Id, cancellationToken);
        ProductPhoto? photo = photos.FirstOrDefault(candidate => candidate.Slot == request.Slot);

        if (request.Content is null)
        {
            if (photo is not null)
            {
                _photos.Remove(photo);
            }
        }
        else if (photo is not null)
        {
            Result replaced = photo.Replace(request.Content);

            if (replaced.IsFailure)
            {
                return replaced;
            }

            Result thumbnail = photo.SetThumbnail(request.Thumbnail);

            if (thumbnail.IsFailure)
            {
                return thumbnail;
            }
        }
        else
        {
            Result<ProductPhoto> created = ProductPhoto.Create(
                product.TenantId, product.FirmId, product.Id, request.Slot, request.Content);

            if (created.IsFailure)
            {
                return created;
            }

            Result thumbnail = created.Value.SetThumbnail(request.Thumbnail);

            if (thumbnail.IsFailure)
            {
                return thumbnail;
            }

            _photos.Add(created.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Finds a product of the selected firm.</summary>
    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<Guid, string>>> Handle(
        ListProductThumbnailsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is not { } firmId
            ? Result.Failure<IReadOnlyDictionary<Guid, string>>(Error.BusinessRule(
                "Firm.NotSelected", "Select a firm first."))
            : Result.Success(await _photos.ListThumbnailsAsync(firmId, cancellationToken));
    }

    private async Task<Result<Product>> FindAsync(Guid productId, CancellationToken cancellationToken)
    {
        Product? product = await _products.FindAsync(ProductId.From(productId), cancellationToken);

        return product is null || product.FirmId != _tenantContext.FirmId
            ? Result.Failure<Product>(Error.NotFound(
                "ProductPhoto.ProductNotFound", "No such product in the selected firm."))
            : Result.Success(product);
    }
}
