using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Platform.Menus;

/// <summary>
/// Produces the navigation menu for the signed-in user, in the selected firm.
/// </summary>
/// <remarks>
/// The menu is data, so this is a read of the firm's tree filtered by what the caller
/// is allowed to reach. Filtering here rather than in the client is not a security
/// measure - every endpoint checks for itself, and a hidden menu entry protects
/// nothing - but a courtesy that has to be got right anyway: a menu offering screens
/// that refuse the person who clicks them is worse than one that is merely short.
/// </remarks>
public sealed record GetMenuQuery : IQuery<MenuResponse>;

/// <summary>One entry in the resolved menu.</summary>
/// <param name="Id">The entry.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">The label to show.</param>
/// <param name="LabelArabic">The label in Arabic, where one is recorded.</param>
/// <param name="Icon">The icon name, if it has one.</param>
/// <param name="Route">The client route it opens, or null for a heading.</param>
/// <param name="Module">The module it belongs to.</param>
/// <param name="Children">The entries beneath it, in display order.</param>
public sealed record MenuEntry(
    Guid Id,
    string Code,
    string Label,
    string? LabelArabic,
    string? Icon,
    string? Route,
    string Module,
    IReadOnlyList<MenuEntry> Children);

/// <summary>The resolved menu.</summary>
/// <param name="Items">The top-level entries, in display order.</param>
public sealed record MenuResponse(IReadOnlyList<MenuEntry> Items);

/// <summary>One menu row as stored, before the tree is assembled.</summary>
/// <param name="Id">The entry.</param>
/// <param name="ParentId">The entry it sits beneath, or null at the top level.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Label">The label.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="Route">The client route, or null for a heading.</param>
/// <param name="Module">The module it belongs to.</param>
/// <param name="SortOrder">Its position among its siblings.</param>
/// <param name="RequiredPermission">The permission needed to see it, if any.</param>
public sealed record MenuItemRow(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Label,
    string? LabelArabic,
    string? Icon,
    string? Route,
    string Module,
    int SortOrder,
    string? RequiredPermission);

/// <summary>Reads a firm's menu rows.</summary>
public interface IMenuReader
{
    /// <summary>Reads every enabled entry for a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries, in no particular order.</returns>
    Task<IReadOnlyList<MenuItemRow>> ReadAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);
}

/// <summary>Handles <see cref="GetMenuQuery"/>.</summary>
public sealed class GetMenuQueryHandler : IQueryHandler<GetMenuQuery, MenuResponse>
{
    /// <summary>The permission code standing for every permission.</summary>
    private const string WildcardPermission = "*";

    private readonly IMenuReader _reader;
    private readonly IPermissionChecker _permissions;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetMenuQueryHandler"/> class.</summary>
    /// <param name="reader">The menu reader.</param>
    /// <param name="permissions">The permission checker.</param>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetMenuQueryHandler(
        IMenuReader reader,
        IPermissionChecker permissions,
        ICurrentUser currentUser,
        ITenantContext tenantContext)
    {
        _reader = reader;
        _permissions = permissions;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<MenuResponse>> Handle(
        GetMenuQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<MenuResponse>(Error.Forbidden(
                "Menu.NoFirmSelected", "A firm must be selected to resolve a menu."));
        }

        // The system actor is not a person and has no menu to render. Checking this
        // rather than trusting UserId to be null matters: it is never null - platform
        // work runs as UserId.System - so a menu resolved without the check would be
        // built from whatever permissions that actor happens to carry.
        if (!_currentUser.IsAuthenticated)
        {
            return Result.Failure<MenuResponse>(Error.Forbidden(
                "Menu.NotSignedIn", "A menu can only be resolved for a signed-in user."));
        }

        IReadOnlyList<MenuItemRow> rows = await _reader.ReadAsync(firmId, cancellationToken);

        IReadOnlySet<string> held = await _permissions.GetPermissionsAsync(
            _currentUser.UserId, cancellationToken);

        // A super administrator holds a stored wildcard rather than several hundred
        // enumerated codes. Testing set membership alone would report false for every
        // specific permission and hide the entire menu from the one account that can
        // reach everything - which is also the account most likely to notice.
        bool holdsEverything = held.Contains(WildcardPermission);

        ILookup<Guid?, MenuItemRow> byParent = rows.ToLookup(row => row.ParentId);

        return Result.Success(new MenuResponse(Build(null, byParent, held, holdsEverything)));
    }

    /// <summary>Assembles one level of the tree, and everything beneath it.</summary>
    /// <param name="parentId">The level's parent, or null for the top level.</param>
    /// <param name="byParent">Every row, grouped by its parent.</param>
    /// <param name="held">The permissions the user holds.</param>
    /// <param name="holdsEverything">Whether the user holds the wildcard.</param>
    /// <returns>The entries at this level that the user may see.</returns>
    /// <remarks>
    /// Depth-first, so a heading is only kept once its children are known. A heading
    /// whose every child was filtered away is dropped rather than rendered empty: an
    /// empty heading advertises something the user cannot reach and invites them to
    /// ask why it does nothing.
    /// </remarks>
    private static List<MenuEntry> Build(
        Guid? parentId,
        ILookup<Guid?, MenuItemRow> byParent,
        IReadOnlySet<string> held,
        bool holdsEverything)
    {
        List<MenuEntry> entries = [];

        foreach (MenuItemRow row in byParent[parentId]
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Label, StringComparer.Ordinal))
        {
            if (!IsPermitted(row, held, holdsEverything))
            {
                continue;
            }

            List<MenuEntry> children = Build(row.Id, byParent, held, holdsEverything);

            // A heading exists to hold things. With nothing left beneath it and
            // nowhere of its own to go, it has no reason to appear.
            if (children.Count == 0 && string.IsNullOrWhiteSpace(row.Route))
            {
                continue;
            }

            entries.Add(new MenuEntry(
                row.Id,
                row.Code,
                row.Label,
                row.LabelArabic,
                row.Icon,
                row.Route,
                row.Module,
                children));
        }

        return entries;
    }

    /// <summary>Whether a user may see one entry.</summary>
    /// <param name="row">The entry.</param>
    /// <param name="held">The permissions the user holds.</param>
    /// <param name="holdsEverything">Whether the user holds the wildcard.</param>
    /// <returns><see langword="true"/> when the entry may be shown.</returns>
    private static bool IsPermitted(
        MenuItemRow row,
        IReadOnlySet<string> held,
        bool holdsEverything) =>
        holdsEverything
        || string.IsNullOrWhiteSpace(row.RequiredPermission)
        || held.Contains(row.RequiredPermission);
}
