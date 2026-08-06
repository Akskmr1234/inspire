using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>
/// A grouping of products, and optionally of other groupings beneath it.
/// </summary>
/// <remarks>
/// The legacy ribbon names Category and Sub Class as separate masters. They are one
/// thing here, arranged as a tree: a sub-class is a category with a parent. Two tables
/// holding the same shape would mean two screens, two sets of rules, and a third table
/// the day somebody wants a level below sub-class - which reporting hierarchies always
/// eventually do.
/// </remarks>
public sealed class Category : AggregateRoot<CategoryId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a category code may be.</summary>
    public const int MaximumCodeLength = 30;

    /// <summary>The longest a category name may be.</summary>
    public const int MaximumNameLength = 100;

    private Category(
        CategoryId id,
        TenantId tenantId,
        FirmId firmId,
        CategoryId? parentId,
        string code,
        string name)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ParentId = parentId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Category()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the category this one sits beneath, or null at the top level.</summary>
    public CategoryId? ParentId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the category name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, for RTL presentation.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets whether the category may still be assigned to a product.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a top-level category.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The category name.</param>
    /// <returns>The category, or a validation failure.</returns>
    public static Result<Category> CreateRoot(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name)
    {
        Result validation = Validate(code, name);

        return validation.IsFailure
            ? Result.Failure<Category>(validation.Error)
            : Result.Success(new Category(
                CategoryId.NewId(), tenantId, firmId, parentId: null,
                code.Trim().ToUpperInvariant(), name.Trim()));
    }

    /// <summary>Creates a category beneath an existing one.</summary>
    /// <param name="parent">The category it sits beneath.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The category name.</param>
    /// <returns>The category, or a validation failure.</returns>
    public static Result<Category> CreateChild(Category parent, string code, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Result validation = Validate(code, name);

        return validation.IsFailure
            ? Result.Failure<Category>(validation.Error)
            : Result.Success(new Category(
                CategoryId.NewId(), parent.TenantId, parent.FirmId, parent.Id,
                code.Trim().ToUpperInvariant(), name.Trim()));
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or null to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Renames the category.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Category.NameRequired", "A category name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Moves the category beneath a different parent, or to the top level.</summary>
    /// <param name="parent">The new parent, or null for the top level.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result MoveTo(Category? parent)
    {
        if (parent is null)
        {
            ParentId = null;
            return Result.Success();
        }

        if (parent.Id == Id)
        {
            return Result.Failure(Error.Validation(
                "Category.CannotParentToSelf",
                $"'{Name}' cannot be placed beneath itself."));
        }

        return parent.FirmId != FirmId
            ? Result.Failure(Error.Validation(
                "Category.DifferentFirm",
                $"'{Name}' cannot be placed beneath a category of another firm."))
            : Apply(parent);
    }

    /// <summary>Stops the category being assigned to new products.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the category to be assigned again.</summary>
    public void Activate() => IsActive = true;

    private Result Apply(Category parent)
    {
        ParentId = parent.Id;
        return Result.Success();
    }

    private static Result Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "Category.CodeRequired", "A category code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure(Error.Validation(
                "Category.CodeTooLong",
                $"A category code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Category.NameRequired", "A category name is required."));
        }

        return name.Trim().Length > MaximumNameLength
            ? Result.Failure(Error.Validation(
                "Category.NameTooLong",
                $"A category name cannot exceed {MaximumNameLength} characters."))
            : Result.Success();
    }
}
