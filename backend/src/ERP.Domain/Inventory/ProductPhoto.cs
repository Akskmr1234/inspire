using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Files;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>One of a product's photographs, kept apart from it so a list of products stays light.</summary>
/// <remarks>
/// Stored as a data URL of a picture the screen has already scaled down. A product may
/// have up to <see cref="MaximumSlots"/>, each in a numbered slot; the first is its main one.
/// </remarks>
public sealed class ProductPhoto : AggregateRoot<ProductPhotoId>, IFirmScoped
{
    /// <summary>The largest picture accepted, as the length of its data URL.</summary>
    public const int MaximumLength = 700_000;

    /// <summary>How many photographs a product may have.</summary>
    public const int MaximumSlots = 3;

    /// <summary>The longest a thumbnail may be, as a data URL: a small picture of about 96px.</summary>
    public const int MaximumThumbnailLength = 40_000;

    private ProductPhoto(
        ProductPhotoId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        int slot)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        Slot = slot;
        Content = string.Empty;
    }

    private ProductPhoto() => Content = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product pictured.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets which of the product's photographs this is, from one.</summary>
    public int Slot { get; private set; }

    /// <summary>Gets the picture, as a data URL, or a reference to it in file storage.</summary>
    public string Content { get; private set; }

    /// <summary>
    /// Gets a small copy of the picture, for lists that show many products at once: the
    /// sales catalog. Made by the browser when the picture is saved; null for a picture
    /// saved before thumbnails were kept.
    /// </summary>
    public string? Thumbnail { get; private set; }

    /// <summary>Records one of a product's photographs.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="slot">Which of its photographs it is, from one.</param>
    /// <param name="content">The picture, as a data URL.</param>
    /// <returns>The photograph, or the reason it was refused.</returns>
    public static Result<ProductPhoto> Create(
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        int slot,
        string content)
    {
        if (slot is < 1 or > MaximumSlots)
        {
            return Result.Failure<ProductPhoto>(Error.Validation(
                "ProductPhoto.SlotInvalid", $"A product has at most {MaximumSlots} photographs."));
        }

        ProductPhoto photo = new(ProductPhotoId.NewId(), tenantId, firmId, productId, slot);
        Result replaced = photo.Replace(content);

        return replaced.IsFailure ? Result.Failure<ProductPhoto>(replaced.Error) : Result.Success(photo);
    }

    /// <summary>Replaces the picture.</summary>
    /// <param name="content">The picture, as a data URL.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Replace(string content)
    {
        if (string.IsNullOrWhiteSpace(content)
            || !content.StartsWith("data:image/", StringComparison.Ordinal)
            || content.Length > MaximumLength)
        {
            return Result.Failure(Error.Validation(
                "ProductPhoto.Invalid",
                "A photograph is a picture of at most about half a megabyte."));
        }

        Content = content;
        return Result.Success();
    }

    /// <summary>
    /// Records that the picture and its thumbnail now live in file storage, keeping only
    /// references to them.
    /// </summary>
    /// <param name="content">The reference to the picture.</param>
    /// <param name="thumbnail">The reference to the thumbnail, or null for none.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>Only the server calls this, with references it made itself.</remarks>
    public Result MoveTo(string content, string? thumbnail)
    {
        if (!StoredFile.IsReference(content) || (thumbnail is not null && !StoredFile.IsReference(thumbnail)))
        {
            return Result.Failure(Error.Validation(
                "ProductPhoto.ReferenceInvalid", "A stored photograph needs a reference to its file."));
        }

        Content = content;
        Thumbnail = thumbnail;
        return Result.Success();
    }

    /// <summary>Sets the small copy of the picture; null clears it.</summary>
    /// <param name="thumbnail">The thumbnail, as a data URL.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetThumbnail(string? thumbnail)
    {
        if (thumbnail is not null
            && (!thumbnail.StartsWith("data:image/", StringComparison.Ordinal)
                || thumbnail.Length > MaximumThumbnailLength))
        {
            return Result.Failure(Error.Validation(
                "ProductPhoto.ThumbnailInvalid",
                "A thumbnail is a small picture of at most about thirty kilobytes."));
        }

        Thumbnail = thumbnail;
        return Result.Success();
    }
}
