using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Menus;

// ---------------------------------------------------------------- administration

/// <summary>
/// Produces the whole menu tree as an administrator needs to see it.
/// </summary>
/// <remarks>
/// Deliberately not the same read as <see cref="GetMenuQuery"/>. That one answers
/// "what may I use", and hides entries the caller lacks the permission for and
/// headings left empty by that filtering. This one answers "what is there", and must
/// show hidden entries - somebody cannot unhide what they cannot see - along with
/// which entries are system entries and what permission each requires.
/// </remarks>
public sealed record GetMenuAdministrationQuery : IQuery<MenuAdministrationResponse>;

/// <summary>One entry as the administration screen shows it.</summary>
/// <param name="Id">The entry.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">The English label.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="Route">The route it opens, or null for a heading.</param>
/// <param name="Module">The module it belongs to.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
/// <param name="SortOrder">Its position among its siblings.</param>
/// <param name="IsEnabled">Whether it is currently shown.</param>
/// <param name="IsSystem">Whether it was seeded and cannot be deleted.</param>
/// <param name="Children">The entries beneath it, in display order.</param>
public sealed record MenuAdministrationEntry(
    Guid Id,
    string Code,
    string Label,
    string? LabelArabic,
    string? Icon,
    string? Route,
    string Module,
    string? RequiredPermission,
    int SortOrder,
    bool IsEnabled,
    bool IsSystem,
    IReadOnlyList<MenuAdministrationEntry> Children);

/// <summary>The whole menu tree.</summary>
/// <param name="Items">The top-level entries, in display order.</param>
public sealed record MenuAdministrationResponse(
    IReadOnlyList<MenuAdministrationEntry> Items);

/// <summary>Reads every menu row of a firm, hidden ones included.</summary>
public interface IMenuAdministrationReader
{
    /// <summary>Reads every entry for a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries, in no particular order.</returns>
    Task<IReadOnlyList<MenuAdministrationRow>> ReadAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);
}

/// <summary>One menu row as stored, for administration.</summary>
/// <param name="Id">The entry.</param>
/// <param name="ParentId">The entry it sits beneath.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">The English label.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="Route">The route it opens.</param>
/// <param name="Module">The module it belongs to.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
/// <param name="SortOrder">Its position among its siblings.</param>
/// <param name="IsEnabled">Whether it is shown.</param>
/// <param name="IsSystem">Whether it was seeded.</param>
public sealed record MenuAdministrationRow(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Label,
    string? LabelArabic,
    string? Icon,
    string? Route,
    string Module,
    string? RequiredPermission,
    int SortOrder,
    bool IsEnabled,
    bool IsSystem);

/// <summary>Handles <see cref="GetMenuAdministrationQuery"/>.</summary>
public sealed class GetMenuAdministrationQueryHandler
    : IQueryHandler<GetMenuAdministrationQuery, MenuAdministrationResponse>
{
    private readonly IMenuAdministrationReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetMenuAdministrationQueryHandler"/> class.</summary>
    /// <param name="reader">The administration reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetMenuAdministrationQueryHandler(
        IMenuAdministrationReader reader,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<MenuAdministrationResponse>> Handle(
        GetMenuAdministrationQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<MenuAdministrationResponse>(Error.Forbidden(
                "Menu.NoFirmSelected", "A firm must be selected to manage a menu."));
        }

        IReadOnlyList<MenuAdministrationRow> rows =
            await _reader.ReadAsync(firmId, cancellationToken);

        ILookup<Guid?, MenuAdministrationRow> byParent = rows.ToLookup(row => row.ParentId);

        return Result.Success(new MenuAdministrationResponse(Build(null, byParent)));
    }

    /// <summary>Assembles one level of the tree, and everything beneath it.</summary>
    /// <param name="parentId">The level's parent, or null for the top level.</param>
    /// <param name="byParent">Every row, grouped by its parent.</param>
    /// <returns>The entries at this level.</returns>
    /// <remarks>
    /// Nothing is filtered. An empty heading is kept here precisely because it is one
    /// of the things an administrator has opened this screen to deal with.
    /// </remarks>
    private static List<MenuAdministrationEntry> Build(
        Guid? parentId,
        ILookup<Guid?, MenuAdministrationRow> byParent) =>
        [.. byParent[parentId]
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Label, StringComparer.Ordinal)
            .Select(row => new MenuAdministrationEntry(
                row.Id,
                row.Code,
                row.Label,
                row.LabelArabic,
                row.Icon,
                row.Route,
                row.Module,
                row.RequiredPermission,
                row.SortOrder,
                row.IsEnabled,
                row.IsSystem,
                Build(row.Id, byParent)))];
}

// ---------------------------------------------------------------------- commands

/// <summary>Adds a menu entry of an administrator's own.</summary>
/// <param name="Code">The stable code, unique within the firm.</param>
/// <param name="Label">The label shown in the interface.</param>
/// <param name="Module">The module the entry belongs to.</param>
/// <param name="ParentId">The entry it sits beneath, or null for the top level.</param>
/// <param name="Route">The route it opens, or null for a heading.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
/// <param name="SortOrder">Its position among its siblings.</param>
public sealed record CreateMenuItemCommand(
    string Code,
    string Label,
    string Module,
    Guid? ParentId = null,
    string? Route = null,
    string? LabelArabic = null,
    string? Icon = null,
    string? RequiredPermission = null,
    int SortOrder = 0) : ICommand<Guid>;

/// <summary>Changes what a menu entry says and where it points.</summary>
/// <param name="MenuItemId">The entry.</param>
/// <param name="Label">The new label.</param>
/// <param name="Route">The route it opens, or null for a heading.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
public sealed record UpdateMenuItemCommand(
    Guid MenuItemId,
    string Label,
    string? Route = null,
    string? LabelArabic = null,
    string? Icon = null,
    string? RequiredPermission = null) : ICommand;

/// <summary>Moves a menu entry to a new parent, a new position, or both.</summary>
/// <param name="MenuItemId">The entry.</param>
/// <param name="ParentId">The new parent, or null for the top level.</param>
/// <param name="SortOrder">The new position among its siblings.</param>
public sealed record MoveMenuItemCommand(
    Guid MenuItemId,
    Guid? ParentId,
    int SortOrder) : ICommand;

/// <summary>Shows or hides a menu entry.</summary>
/// <param name="MenuItemId">The entry.</param>
/// <param name="IsEnabled">Whether it should be shown.</param>
public sealed record SetMenuItemVisibilityCommand(
    Guid MenuItemId,
    bool IsEnabled) : ICommand;

/// <summary>Deletes a menu entry an administrator added.</summary>
/// <param name="MenuItemId">The entry.</param>
public sealed record DeleteMenuItemCommand(Guid MenuItemId) : ICommand;

/// <summary>Validates a <see cref="CreateMenuItemCommand"/>.</summary>
public sealed class CreateMenuItemCommandValidator : AbstractValidator<CreateMenuItemCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateMenuItemCommandValidator"/> class.</summary>
    public CreateMenuItemCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(MenuItem.MaximumCodeLength);
        RuleFor(c => c.Label).NotEmpty().MaximumLength(MenuItem.MaximumLabelLength);
        RuleFor(c => c.Module).NotEmpty().MaximumLength(MenuItem.MaximumModuleLength);
        RuleFor(c => c.Route!).MaximumLength(MenuItem.MaximumRouteLength)
            .When(c => c.Route is not null);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Validates an <see cref="UpdateMenuItemCommand"/>.</summary>
public sealed class UpdateMenuItemCommandValidator : AbstractValidator<UpdateMenuItemCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateMenuItemCommandValidator"/> class.</summary>
    public UpdateMenuItemCommandValidator()
    {
        RuleFor(c => c.MenuItemId).NotEmpty();
        RuleFor(c => c.Label).NotEmpty().MaximumLength(MenuItem.MaximumLabelLength);
        RuleFor(c => c.Route!).MaximumLength(MenuItem.MaximumRouteLength)
            .When(c => c.Route is not null);
    }
}

/// <summary>Validates a <see cref="MoveMenuItemCommand"/>.</summary>
public sealed class MoveMenuItemCommandValidator : AbstractValidator<MoveMenuItemCommand>
{
    /// <summary>Initialises a new instance of the <see cref="MoveMenuItemCommandValidator"/> class.</summary>
    public MoveMenuItemCommandValidator()
    {
        RuleFor(c => c.MenuItemId).NotEmpty();
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Shared resolution for the menu commands.</summary>
internal static class MenuContext
{
    /// <summary>Resolves an entry, refusing one belonging to another firm.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="menuItemId">The entry being acted on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entry, or the reason it could not be resolved.</returns>
    /// <remarks>
    /// The firm check reports not-found rather than forbidden. Distinguishing the two
    /// would confirm that an entry exists in a firm the caller cannot see, which is
    /// more than they are entitled to learn from a menu identifier they guessed.
    /// </remarks>
    internal static async Task<Result<MenuItem>> ResolveAsync(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        Guid menuItemId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<MenuItem>(Error.Forbidden(
                "Menu.NoFirmSelected", "A firm must be selected to manage a menu."));
        }

        MenuItem? item = await items.FindAsync(
            MenuItemId.From(menuItemId), cancellationToken);

        return item is null || item.FirmId != firmId
            ? Result.Failure<MenuItem>(Error.NotFound(
                "MenuItem.NotFound", "No such menu entry in the selected firm."))
            : Result.Success(item);
    }
}

/// <summary>Handles <see cref="CreateMenuItemCommand"/>.</summary>
public sealed class CreateMenuItemCommandHandler
    : ICommandHandler<CreateMenuItemCommand, Guid>
{
    private readonly IMenuItemRepository _items;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CreateMenuItemCommandHandler"/> class.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CreateMenuItemCommandHandler(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _items = items;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        CreateMenuItemCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Guid>(Error.Forbidden(
                "Menu.NoFirmSelected", "A firm must be selected to manage a menu."));
        }

        TenantId tenantId = _tenantContext.TenantId;

        string code = request.Code.Trim().ToLowerInvariant();

        if (await _items.CodeExistsAsync(firmId, code, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "MenuItem.CodeTaken", $"A menu entry with the code '{code}' already exists."));
        }

        MenuItem? parent = null;

        if (request.ParentId is { } parentId)
        {
            Result<MenuItem> found = await MenuContext.ResolveAsync(
                _items, _tenantContext, parentId, cancellationToken);

            if (found.IsFailure)
            {
                return Result.Failure<Guid>(found.Error);
            }

            parent = found.Value;
        }

        // Entries an administrator adds are never system entries, so they can be
        // deleted again. Only the seeder creates undeletable ones.
        Result<MenuItem> created = parent is null
            ? MenuItem.CreateRoot(
                tenantId, firmId, code, request.Label, request.Module, request.SortOrder)
            : MenuItem.CreateChild(
                parent, code, request.Label, request.SortOrder, request.Module);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        MenuItem item = created.Value;

        Result routed = item.SetRoute(request.Route);

        if (routed.IsFailure)
        {
            return Result.Failure<Guid>(routed.Error);
        }

        item.SetArabicLabel(request.LabelArabic);
        item.SetIcon(request.Icon);
        item.RequirePermission(request.RequiredPermission);

        _items.Add(item);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(item.Id.Value);
    }
}

/// <summary>Handles <see cref="UpdateMenuItemCommand"/>.</summary>
public sealed class UpdateMenuItemCommandHandler : ICommandHandler<UpdateMenuItemCommand>
{
    private readonly IMenuItemRepository _items;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="UpdateMenuItemCommandHandler"/> class.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public UpdateMenuItemCommandHandler(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _items = items;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        UpdateMenuItemCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<MenuItem> found = await MenuContext.ResolveAsync(
            _items, _tenantContext, request.MenuItemId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        MenuItem item = found.Value;

        // A system entry is renamed and repointed as freely as any other. Only
        // deletion is refused, so an administrator can translate or re-label the
        // seeded menu to match how their firm actually talks about these screens.
        Result renamed = item.Rename(request.Label);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        Result routed = item.SetRoute(request.Route);

        if (routed.IsFailure)
        {
            return routed;
        }

        item.SetArabicLabel(request.LabelArabic);
        item.SetIcon(request.Icon);
        item.RequirePermission(request.RequiredPermission);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="MoveMenuItemCommand"/>.</summary>
public sealed class MoveMenuItemCommandHandler : ICommandHandler<MoveMenuItemCommand>
{
    private readonly IMenuItemRepository _items;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="MoveMenuItemCommandHandler"/> class.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public MoveMenuItemCommandHandler(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _items = items;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        MoveMenuItemCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<MenuItem> found = await MenuContext.ResolveAsync(
            _items, _tenantContext, request.MenuItemId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        MenuItem item = found.Value;
        MenuItem? parent = null;

        if (request.ParentId is { } parentId)
        {
            Result<MenuItem> foundParent = await MenuContext.ResolveAsync(
                _items, _tenantContext, parentId, cancellationToken);

            if (foundParent.IsFailure)
            {
                return Result.Failure(foundParent.Error);
            }

            parent = foundParent.Value;

            // A move onto a descendant would detach the subtree from the tree
            // entirely, leaving a ring of entries that reference each other and
            // nothing that reaches them. The aggregate refuses the self case; the
            // deeper one needs the tree, which only this layer has.
            if (await IsDescendantAsync(parent, item.Id, cancellationToken))
            {
                return Result.Failure(Error.Validation(
                    "MenuItem.CannotParentToDescendant",
                    $"'{item.Label}' cannot be placed beneath one of its own children."));
            }
        }

        Result moved = item.MoveTo(parent);

        if (moved.IsFailure)
        {
            return moved;
        }

        Result reordered = item.Reorder(request.SortOrder);

        if (reordered.IsFailure)
        {
            return reordered;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Walks up from an entry looking for an ancestor.</summary>
    /// <param name="candidate">The entry to start from.</param>
    /// <param name="ancestorId">The ancestor being looked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the ancestor is found above the candidate.</returns>
    /// <remarks>
    /// Walks upward rather than downward: a menu is shallow, so the number of steps is
    /// the depth of the tree rather than the size of a subtree.
    /// </remarks>
    private async Task<bool> IsDescendantAsync(
        MenuItem candidate,
        MenuItemId ancestorId,
        CancellationToken cancellationToken)
    {
        MenuItemId? parentId = candidate.ParentId;

        while (parentId is { } id)
        {
            if (id == ancestorId)
            {
                return true;
            }

            MenuItem? parent = await _items.FindAsync(id, cancellationToken);

            if (parent is null)
            {
                return false;
            }

            parentId = parent.ParentId;
        }

        return false;
    }
}

/// <summary>Handles <see cref="SetMenuItemVisibilityCommand"/>.</summary>
public sealed class SetMenuItemVisibilityCommandHandler
    : ICommandHandler<SetMenuItemVisibilityCommand>
{
    private readonly IMenuItemRepository _items;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SetMenuItemVisibilityCommandHandler"/> class.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SetMenuItemVisibilityCommandHandler(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _items = items;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SetMenuItemVisibilityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<MenuItem> found = await MenuContext.ResolveAsync(
            _items, _tenantContext, request.MenuItemId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (request.IsEnabled)
        {
            found.Value.Enable();
        }
        else
        {
            found.Value.Disable();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Handles <see cref="DeleteMenuItemCommand"/>.</summary>
public sealed class DeleteMenuItemCommandHandler : ICommandHandler<DeleteMenuItemCommand>
{
    private readonly IMenuItemRepository _items;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="DeleteMenuItemCommandHandler"/> class.</summary>
    /// <param name="items">The menu repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public DeleteMenuItemCommandHandler(
        IMenuItemRepository items,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _items = items;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        DeleteMenuItemCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<MenuItem> found = await MenuContext.ResolveAsync(
            _items, _tenantContext, request.MenuItemId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        MenuItem item = found.Value;
        Result deletable = item.EnsureDeletable();

        if (deletable.IsFailure)
        {
            return deletable;
        }

        // Refused rather than cascaded. Deleting a heading that still holds screens
        // would remove them from the menu with nothing to say where they went, and
        // the entries beneath a system heading cannot themselves be recreated.
        int children = await _items.CountChildrenAsync(item.Id, cancellationToken);

        if (children > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "MenuItem.HasChildren",
                $"'{item.Label}' still holds {children} entries. Move them elsewhere " +
                $"before deleting it."));
        }

        _items.Remove(item);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
