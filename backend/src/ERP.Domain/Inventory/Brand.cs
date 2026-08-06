using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>The maker or marque a product is sold under.</summary>
/// <remarks>
/// Flat, unlike <see cref="Category"/>. A brand is a name a product carries rather
/// than a place in a hierarchy, and nothing in the specification's reporting asks for
/// a brand beneath a brand.
/// </remarks>
public sealed class Brand : AggregateRoot<BrandId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a brand code may be.</summary>
    public const int MaximumCodeLength = 30;

    /// <summary>The longest a brand name may be.</summary>
    public const int MaximumNameLength = 100;

    private Brand(BrandId id, TenantId tenantId, FirmId firmId, string code, string name)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Brand()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the brand name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, for RTL presentation.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets whether the brand may still be assigned to a product.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a brand.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The brand name.</param>
    /// <returns>The brand, or a validation failure.</returns>
    public static Result<Brand> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Brand>(Error.Validation(
                "Brand.CodeRequired", "A brand code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<Brand>(Error.Validation(
                "Brand.CodeTooLong",
                $"A brand code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Brand>(Error.Validation(
                "Brand.NameRequired", "A brand name is required."));
        }

        return name.Trim().Length > MaximumNameLength
            ? Result.Failure<Brand>(Error.Validation(
                "Brand.NameTooLong",
                $"A brand name cannot exceed {MaximumNameLength} characters."))
            : Result.Success(new Brand(
                BrandId.NewId(), tenantId, firmId,
                code.Trim().ToUpperInvariant(), name.Trim()));
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or null to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Renames the brand.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Brand.NameRequired", "A brand name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Stops the brand being assigned to new products.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Allows the brand to be assigned again.</summary>
    public void Activate() => IsActive = true;
}
