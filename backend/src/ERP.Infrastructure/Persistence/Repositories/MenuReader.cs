using ERP.Application.Platform.Menus;
using ERP.Domain.Platform;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads a firm's menu rows.</summary>
/// <remarks>
/// The whole tree comes back in one query rather than a level at a time. A menu is a
/// few dozen rows and is read on every page load, so one round trip that returns all
/// of it beats a recursive walk issuing a query per level - and the handler needs the
/// full set anyway to decide which headings survive their children being filtered.
/// <para>
/// Entries an administrator has switched off are excluded here rather than in the
/// handler. A disabled entry is not a permission decision and never becomes visible to
/// anybody, so there is no reason to carry it across the layer boundary.
/// </para>
/// </remarks>
public sealed class MenuReader : IMenuReader
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="MenuReader"/> class.</summary>
    /// <param name="context">The database context.</param>
    public MenuReader(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuItemRow>> ReadAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        await _context.MenuItems
            .Where(item => item.FirmId == firmId && item.IsEnabled)
            .Select(item => new MenuItemRow(
                item.Id.Value,
                item.ParentId == null ? null : item.ParentId.Value.Value,
                item.Code,
                item.Label,
                item.LabelArabic,
                item.Icon,
                item.Route,
                item.Module,
                item.SortOrder,
                item.RequiredPermission))
            .ToListAsync(cancellationToken);
}
