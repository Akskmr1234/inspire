using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>
/// One entry in the navigation menu: a heading, or a link to a screen.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires that an administrator can show, hide, reorder, regroup,
/// and move entries between modules with no source-code change. That is only possible
/// if the menu is data, so it is - one row per entry, arranged into a tree by
/// <see cref="ParentId"/> and ordered by <see cref="SortOrder"/>.
/// </para>
/// <para>
/// An entry carries the permission its screen requires rather than a list of who may
/// see it. Visibility is then a consequence of authorisation rather than a second
/// thing to maintain beside it: grant somebody the cheque permission and the cheque
/// screens appear, revoke it and they go. The alternative - assigning menus to roles
/// directly - guarantees that the two eventually disagree, and the failure is a user
/// staring at a menu entry that refuses them, or worse, one they should have been
/// offered and never knew existed.
/// </para>
/// <para>
/// Scoped to a firm rather than a tenant. A group operating a trading company and a
/// service workshop under one tenant needs different menus for each, and a per-firm
/// tree makes that the ordinary case rather than an exception carved out later. The
/// row count is trivial - a few dozen per firm.
/// </para>
/// </remarks>
public sealed class MenuItem : AggregateRoot<MenuItemId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a menu code may be.</summary>
    public const int MaximumCodeLength = 100;

    /// <summary>The longest a label may be.</summary>
    public const int MaximumLabelLength = 100;

    /// <summary>The longest a route may be.</summary>
    public const int MaximumRouteLength = 200;

    /// <summary>The longest a module name may be.</summary>
    public const int MaximumModuleLength = 50;

    /// <summary>The longest an icon name may be.</summary>
    public const int MaximumIconLength = 50;

    private MenuItem(
        MenuItemId id,
        TenantId tenantId,
        FirmId firmId,
        MenuItemId? parentId,
        string code,
        string label,
        string module,
        int sortOrder,
        bool isSystem)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ParentId = parentId;
        Code = code;
        Label = label;
        Module = module;
        SortOrder = sortOrder;
        IsSystem = isSystem;
        IsEnabled = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private MenuItem()
    {
        Code = string.Empty;
        Label = string.Empty;
        Module = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the entry this one sits beneath, or null for a top-level entry.</summary>
    public MenuItemId? ParentId { get; private set; }

    /// <summary>Gets the stable code, unique within the firm.</summary>
    /// <remarks>
    /// What a deployment refers to an entry by. Labels are translated and reordered
    /// and are no use as an identifier; an identifier that changed when somebody
    /// renamed a menu would break every reference to it.
    /// </remarks>
    public string Code { get; private set; }

    /// <summary>Gets the label shown in the interface.</summary>
    public string Label { get; private set; }

    /// <summary>Gets the label in Arabic, for RTL presentation.</summary>
    public string? LabelArabic { get; private set; }

    /// <summary>Gets the icon name, if the entry shows one.</summary>
    public string? Icon { get; private set; }

    /// <summary>Gets the client route this entry opens.</summary>
    /// <remarks>
    /// Null for a heading, which groups the entries beneath it and navigates nowhere
    /// itself. A heading whose children are all hidden is hidden with them: an empty
    /// heading is a promise of something that is not there.
    /// </remarks>
    public string? Route { get; private set; }

    /// <summary>Gets the module the entry belongs to.</summary>
    /// <remarks>
    /// Recorded separately from the position in the tree, because the specification
    /// asks for entries to be surfaced under a different module without moving what
    /// they are - a stock report shown under Accounts is still an inventory report.
    /// </remarks>
    public string Module { get; private set; }

    /// <summary>Gets the position among its siblings, ascending.</summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Gets the <c>module:resource:verb</c> permission a user must hold to see this
    /// entry, or null when it is shown to everybody.
    /// </summary>
    public string? RequiredPermission { get; private set; }

    /// <summary>Gets whether an administrator has left the entry switched on.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets whether the entry was seeded and cannot be deleted.</summary>
    /// <remarks>
    /// System entries may still be renamed, reordered, and hidden - everything the
    /// specification asks for - but not removed, because the screen behind one goes on
    /// existing whether or not the menu mentions it, and a deleted entry is far harder
    /// to discover than a hidden one.
    /// </remarks>
    public bool IsSystem { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether this entry navigates somewhere rather than grouping.</summary>
    public bool IsLink => !string.IsNullOrWhiteSpace(Route);

    /// <summary>Creates a top-level entry.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="label">The label shown in the interface.</param>
    /// <param name="module">The module the entry belongs to.</param>
    /// <param name="sortOrder">The position among its siblings.</param>
    /// <param name="isSystem">Whether the entry is seeded and undeletable.</param>
    /// <returns>The entry, or a validation failure.</returns>
    public static Result<MenuItem> CreateRoot(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string label,
        string module,
        int sortOrder = 0,
        bool isSystem = false)
    {
        Result validation = Validate(code, label, module, sortOrder);

        return validation.IsFailure
            ? Result.Failure<MenuItem>(validation.Error)
            : Result.Success(new MenuItem(
                MenuItemId.NewId(), tenantId, firmId, parentId: null,
                code.Trim().ToLowerInvariant(), label.Trim(), module.Trim().ToLowerInvariant(),
                sortOrder, isSystem));
    }

    /// <summary>Creates an entry beneath an existing one.</summary>
    /// <param name="parent">The entry it sits beneath.</param>
    /// <param name="code">The stable code.</param>
    /// <param name="label">The label shown in the interface.</param>
    /// <param name="sortOrder">The position among its siblings.</param>
    /// <param name="module">The module, defaulting to the parent's.</param>
    /// <param name="isSystem">Whether the entry is seeded and undeletable.</param>
    /// <returns>The entry, or a validation failure.</returns>
    /// <remarks>
    /// Tenant and firm come from the parent rather than being supplied, which makes a
    /// child in a different firm from its parent impossible to construct rather than
    /// merely invalid.
    /// </remarks>
    public static Result<MenuItem> CreateChild(
        MenuItem parent,
        string code,
        string label,
        int sortOrder = 0,
        string? module = null,
        bool isSystem = false)
    {
        ArgumentNullException.ThrowIfNull(parent);

        string effectiveModule = module ?? parent.Module;
        Result validation = Validate(code, label, effectiveModule, sortOrder);

        return validation.IsFailure
            ? Result.Failure<MenuItem>(validation.Error)
            : Result.Success(new MenuItem(
                MenuItemId.NewId(), parent.TenantId, parent.FirmId, parent.Id,
                code.Trim().ToLowerInvariant(), label.Trim(),
                effectiveModule.Trim().ToLowerInvariant(), sortOrder, isSystem));
    }

    /// <summary>Points the entry at a client route.</summary>
    /// <param name="route">The route, or null to make it a heading.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result SetRoute(string? route)
    {
        if (route is not null && route.Trim().Length > MaximumRouteLength)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.RouteTooLong",
                $"A route cannot exceed {MaximumRouteLength} characters."));
        }

        Route = string.IsNullOrWhiteSpace(route) ? null : route.Trim();
        return Result.Success();
    }

    /// <summary>Requires a permission before the entry is shown.</summary>
    /// <param name="permissionCode">
    /// The <c>module:resource:verb</c> code, or null to show it to everybody.
    /// </param>
    public void RequirePermission(string? permissionCode) =>
        RequiredPermission = string.IsNullOrWhiteSpace(permissionCode)
            ? null
            : permissionCode.Trim().ToLowerInvariant();

    /// <summary>Sets the icon shown beside the label.</summary>
    /// <param name="icon">The icon name, or null to clear it.</param>
    public void SetIcon(string? icon) =>
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();

    /// <summary>Sets the Arabic label shown in RTL mode.</summary>
    /// <param name="labelArabic">The Arabic label, or null to clear it.</param>
    public void SetArabicLabel(string? labelArabic) =>
        LabelArabic = string.IsNullOrWhiteSpace(labelArabic) ? null : labelArabic.Trim();

    /// <summary>Renames the entry.</summary>
    /// <param name="label">The new label.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Rename(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Failure(Error.Validation(
                "MenuItem.LabelRequired", "A menu label is required."));
        }

        if (label.Trim().Length > MaximumLabelLength)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.LabelTooLong",
                $"A menu label cannot exceed {MaximumLabelLength} characters."));
        }

        Label = label.Trim();
        return Result.Success();
    }

    /// <summary>Moves the entry to a new position among its siblings.</summary>
    /// <param name="sortOrder">The new position.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Reorder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.SortOrderNegative", "A sort order cannot be negative."));
        }

        SortOrder = sortOrder;
        return Result.Success();
    }

    /// <summary>Moves the entry beneath a different parent, or to the top level.</summary>
    /// <param name="parent">The new parent, or null to move it to the top level.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// An entry cannot be moved beneath itself. That is the one cycle reachable in a
    /// single move, and it would produce a subtree that renders forever - a check
    /// worth having at the point somebody tries rather than in the renderer.
    /// </remarks>
    public Result MoveTo(MenuItem? parent)
    {
        if (parent is null)
        {
            ParentId = null;
            return Result.Success();
        }

        if (parent.Id == Id)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.CannotParentToSelf",
                $"'{Label}' cannot be placed beneath itself."));
        }

        if (parent.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.DifferentFirm",
                $"'{Label}' cannot be placed beneath an entry belonging to another firm."));
        }

        ParentId = parent.Id;
        return Result.Success();
    }

    /// <summary>Shows the entry.</summary>
    public void Enable() => IsEnabled = true;

    /// <summary>Hides the entry without deleting it.</summary>
    public void Disable() => IsEnabled = false;

    /// <summary>Checks whether the entry may be deleted.</summary>
    /// <returns>Success, or the reason it may not.</returns>
    public Result EnsureDeletable() => IsSystem
        ? Result.Failure(Error.BusinessRule(
            "MenuItem.SystemEntry",
            $"'{Label}' is a system menu entry and cannot be deleted. Hide it instead."))
        : Result.Success();

    private static Result Validate(string code, string label, string module, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "MenuItem.CodeRequired", "A menu code is required."));
        }

        if (code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.CodeTooLong",
                $"A menu code cannot exceed {MaximumCodeLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return Result.Failure(Error.Validation(
                "MenuItem.LabelRequired", "A menu label is required."));
        }

        if (label.Trim().Length > MaximumLabelLength)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.LabelTooLong",
                $"A menu label cannot exceed {MaximumLabelLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(module))
        {
            return Result.Failure(Error.Validation(
                "MenuItem.ModuleRequired", "A menu entry must belong to a module."));
        }

        if (module.Trim().Length > MaximumModuleLength)
        {
            return Result.Failure(Error.Validation(
                "MenuItem.ModuleTooLong",
                $"A module name cannot exceed {MaximumModuleLength} characters."));
        }

        return sortOrder < 0
            ? Result.Failure(Error.Validation(
                "MenuItem.SortOrderNegative", "A sort order cannot be negative."))
            : Result.Success();
    }
}
