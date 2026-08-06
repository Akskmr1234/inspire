using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>
/// A place stock is held: a warehouse, called a godown in the reference application
/// and a stock location in the interface.
/// </summary>
/// <remarks>
/// <para>
/// Attached to a branch rather than only to a firm, because stock is physically
/// somewhere and a branch is the nearest thing the platform models to a place. A
/// warehouse without a branch is allowed - a central store serving every branch is an
/// ordinary arrangement - but where one is named, transfers between branches become
/// visible as what they are rather than as movements within a single pool.
/// </para>
/// <para>
/// One warehouse is marked the default, and exactly one. The sales screen fills the
/// godown in from it, and a firm with two defaults would fill it in differently
/// depending on which row came back first.
/// </para>
/// </remarks>
public sealed class Warehouse : AggregateRoot<WarehouseId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a warehouse code may be.</summary>
    public const int MaximumCodeLength = 30;

    /// <summary>The longest a warehouse name may be.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>The longest an address may be.</summary>
    public const int MaximumAddressLength = 500;

    private Warehouse(
        WarehouseId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId? branchId,
        string code,
        string name)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BranchId = branchId;
        Code = code;
        Name = name;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch this warehouse belongs to, if any.</summary>
    public BranchId? BranchId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the warehouse name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic, for RTL presentation.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets where the warehouse is.</summary>
    public string? Address { get; private set; }

    /// <summary>Gets whether new documents default to this warehouse.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Gets whether stock may still be moved into or out of it.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a warehouse.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The code, unique within the firm.</param>
    /// <param name="name">The warehouse name.</param>
    /// <param name="branchId">The branch it belongs to, or null for a central store.</param>
    /// <returns>The warehouse, or a validation failure.</returns>
    public static Result<Warehouse> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        BranchId? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Warehouse>(Error.Validation(
                "Warehouse.CodeRequired", "A warehouse code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<Warehouse>(Error.Validation(
                "Warehouse.CodeTooLong",
                $"A warehouse code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Warehouse>(Error.Validation(
                "Warehouse.NameRequired", "A warehouse name is required."));
        }

        return name.Trim().Length > MaximumNameLength
            ? Result.Failure<Warehouse>(Error.Validation(
                "Warehouse.NameTooLong",
                $"A warehouse name cannot exceed {MaximumNameLength} characters."))
            : Result.Success(new Warehouse(
                WarehouseId.NewId(), tenantId, firmId, branchId,
                code.Trim().ToUpperInvariant(), name.Trim()));
    }

    /// <summary>Sets the Arabic name shown in RTL mode.</summary>
    /// <param name="nameArabic">The Arabic name, or null to clear it.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Records where the warehouse is.</summary>
    /// <param name="address">The address, or null to clear it.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetAddress(string? address)
    {
        if (address is not null && address.Trim().Length > MaximumAddressLength)
        {
            return Result.Failure(Error.Validation(
                "Warehouse.AddressTooLong",
                $"An address cannot exceed {MaximumAddressLength} characters."));
        }

        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        return Result.Success();
    }

    /// <summary>Renames the warehouse.</summary>
    /// <param name="name">The new name.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Warehouse.NameRequired", "A warehouse name is required."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Makes this the warehouse new documents default to.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Only one warehouse may be the default, and the uniqueness of that is enforced by
    /// a filtered index in the database rather than here - an aggregate can only see
    /// itself, and two concurrent requests each making a different warehouse the
    /// default would both believe they were correct.
    /// <para>
    /// A deactivated warehouse cannot be the default, because the default is what a
    /// document fills itself in with, and offering one nobody may post to would put the
    /// error at the end of data entry rather than the start.
    /// </para>
    /// </remarks>
    public Result MakeDefault()
    {
        if (!IsActive)
        {
            return Result.Failure(Error.BusinessRule(
                "Warehouse.InactiveCannotBeDefault",
                $"'{Name}' is not active and cannot be the default warehouse."));
        }

        IsDefault = true;
        return Result.Success();
    }

    /// <summary>Stops this being the warehouse new documents default to.</summary>
    public void ClearDefault() => IsDefault = false;

    /// <summary>Stops stock being moved into or out of the warehouse.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The default warehouse cannot be deactivated while it holds that role. Every new
    /// document would otherwise fill itself in with a location it is not allowed to
    /// use, which reads as the software being broken rather than as a setting needing
    /// changed.
    /// </remarks>
    public Result Deactivate()
    {
        if (IsDefault)
        {
            return Result.Failure(Error.BusinessRule(
                "Warehouse.DefaultCannotBeDeactivated",
                $"'{Name}' is the default warehouse. Make another the default first."));
        }

        IsActive = false;
        return Result.Success();
    }

    /// <summary>Allows stock to be moved into and out of the warehouse again.</summary>
    public void Activate() => IsActive = true;
}
