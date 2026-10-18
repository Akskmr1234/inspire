using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Inventory.Masters;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using FluentValidation;

namespace ERP.Application.Inventory.Products;

/// <summary>Files a product under a category and brand, and gives it a tax rate.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="CategoryId">The category it belongs to.</param>
/// <param name="BrandId">Its brand, or null for none.</param>
/// <param name="TaxSlabId">Its tax rate, or null for none.</param>
/// <remarks>
/// The three lookups a product is chosen by and reported under, corrected together. None
/// of them changes a quantity or a value already recorded, which is why they may change
/// at any time while the stock unit may not.
/// </remarks>
public sealed record ClassifyProductCommand(
    Guid ProductId,
    Guid CategoryId,
    Guid? BrandId = null,
    Guid? TaxSlabId = null) : ICommand;

/// <summary>Validates <see cref="ClassifyProductCommand"/>.</summary>
public sealed class ClassifyProductCommandValidator : AbstractValidator<ClassifyProductCommand>
{
    /// <summary>Initialises a new instance of the <see cref="ClassifyProductCommandValidator"/> class.</summary>
    public ClassifyProductCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.CategoryId).NotEmpty();
    }
}

/// <summary>Handles <see cref="ClassifyProductCommand"/>.</summary>
public sealed class ClassifyProductCommandHandler : ICommandHandler<ClassifyProductCommand>
{
    private readonly IProductRepository _products;
    private readonly IInventoryMasterRepository _masters;
    private readonly ITaxSlabRepository _slabs;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ClassifyProductCommandHandler"/> class.</summary>
    /// <param name="products">The product repository.</param>
    /// <param name="masters">The inventory master repository.</param>
    /// <param name="slabs">The tax rate repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ClassifyProductCommandHandler(
        IProductRepository products,
        IInventoryMasterRepository masters,
        ITaxSlabRepository slabs,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _masters = masters;
        _slabs = slabs;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        ClassifyProductCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Product> found = await ProductContext.ResolveAsync(
            _products, _tenantContext, request.ProductId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Product product = found.Value;

        Category? category = await _masters.FindCategoryAsync(
            CategoryId.From(request.CategoryId), cancellationToken);

        if (category is null || category.FirmId != product.FirmId)
        {
            return Result.Failure(Error.NotFound(
                "Product.CategoryNotFound", "No such category in the selected firm."));
        }

        Brand? brand = null;

        if (request.BrandId is { } brandId)
        {
            brand = await _masters.FindBrandAsync(BrandId.From(brandId), cancellationToken);

            if (brand is null || brand.FirmId != product.FirmId)
            {
                return Result.Failure(Error.NotFound(
                    "Product.BrandNotFound", "No such brand in the selected firm."));
            }
        }

        TaxSlab? slab = null;

        if (request.TaxSlabId is { } slabId)
        {
            slab = await _slabs.FindAsync(TaxSlabId.From(slabId), cancellationToken);

            if (slab is null || slab.FirmId != product.FirmId)
            {
                return Result.Failure(Error.NotFound(
                    "Product.TaxSlabNotFound", "No such tax rate in the selected firm."));
            }
        }

        Result applied = category.Id == product.CategoryId
            ? Result.Success()
            : product.ReclassifyTo(category);

        if (applied.IsSuccess && brand?.Id != product.BrandId)
        {
            applied = product.SetBrand(brand);
        }

        if (applied.IsSuccess)
        {
            applied = product.SetTaxSlab(slab);
        }

        if (applied.IsFailure)
        {
            return applied;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
