using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Menus;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Editing the navigation menu.</summary>
/// <remarks>
/// Separate from the menu the client renders, and behind a different permission.
/// Reading your own menu is something every signed-in user does on every page load;
/// rearranging the menu everybody else sees is an administrative act, and the two
/// should not be reachable with the same grant.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/menu")]
[Authorize]
[Produces("application/json")]
public sealed class MenuAdministrationController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="MenuAdministrationController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public MenuAdministrationController(ISender sender) => _sender = sender;

    /// <summary>Returns the whole menu tree, hidden entries included.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every entry, in display order, with its visibility and system flag.</returns>
    /// <remarks>
    /// Unlike <c>GET /menu</c>, nothing is filtered: an administrator cannot switch
    /// something back on that the screen never showed them, and an empty heading is
    /// one of the things they have opened this screen to deal with.
    /// </remarks>
    /// <response code="200">The whole tree.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet]
    [RequiresPermission("platform", "menu", "view")]
    [ProducesResponseType(typeof(MenuAdministrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        Result<MenuAdministrationResponse> result = await _sender.Send(
            new GetMenuAdministrationQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a menu entry.</summary>
    /// <param name="request">The entry to add.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new entry's identifier.</returns>
    /// <remarks>
    /// Entries added here are never system entries, so they can be deleted again.
    /// Only the seeder creates undeletable ones.
    /// </remarks>
    /// <response code="201">Created.</response>
    /// <response code="400">The entry is invalid.</response>
    /// <response code="409">The code is already used in this firm.</response>
    [HttpPost]
    [RequiresPermission("platform", "menu", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateMenuItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new CreateMenuItemCommand(
                request.Code,
                request.Label,
                request.Module,
                request.ParentId,
                request.Route,
                request.LabelArabic,
                request.Icon,
                request.RequiredPermission,
                request.SortOrder),
            cancellationToken);

        // The tree, not a per-entry route: entries are only ever read as part of the
        // whole menu, and there is no endpoint that returns one on its own. Naming an
        // action that cannot be routed to would fail at URL generation rather than at
        // the request, which is a 500 for a request that entirely succeeded.
        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Changes what an entry says and where it points.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="request">The new label, route, and permission.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// A system entry may be relabelled and repointed as freely as any other, so a
    /// firm can make the seeded menu match how it actually talks about these screens.
    /// Only deletion is refused.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such entry in the selected firm.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "menu", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateMenuItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new UpdateMenuItemCommand(
                id,
                request.Label,
                request.Route,
                request.LabelArabic,
                request.Icon,
                request.RequiredPermission),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Moves an entry to a new parent, a new position, or both.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="request">Where it should go.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// An entry cannot be moved beneath itself or beneath one of its own children:
    /// either would detach the subtree from the tree, leaving entries that reference
    /// each other and nothing that reaches them.
    /// </remarks>
    /// <response code="204">Moved.</response>
    /// <response code="400">The move would create a cycle.</response>
    /// <response code="404">No such entry in the selected firm.</response>
    [HttpPost("{id:guid}/move")]
    [RequiresPermission("platform", "menu", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveAsync(
        Guid id,
        [FromBody] MoveMenuItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new MoveMenuItemCommand(id, request.ParentId, request.SortOrder),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Shows or hides an entry.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="request">Whether it should be shown.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Hiding is how a seeded entry is taken off the menu, since it cannot be deleted.
    /// The screen behind it goes on existing and its permission still governs access -
    /// a hidden entry is a tidier menu, not a closed door.
    /// </remarks>
    /// <response code="204">Updated.</response>
    /// <response code="404">No such entry in the selected firm.</response>
    [HttpPost("{id:guid}/visibility")]
    [RequiresPermission("platform", "menu", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetVisibilityAsync(
        Guid id,
        [FromBody] SetMenuItemVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetMenuItemVisibilityCommand(id, request.IsEnabled), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Deletes an entry an administrator added.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Seeded entries are refused - hide them instead - as is any entry that still
    /// holds others, so a heading cannot take a subtree of screens with it.
    /// </remarks>
    /// <response code="204">Deleted.</response>
    /// <response code="404">No such entry in the selected firm.</response>
    /// <response code="422">It is a system entry, or it still holds other entries.</response>
    [HttpDelete("{id:guid}")]
    [RequiresPermission("platform", "menu", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new DeleteMenuItemCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Adding a menu entry.</summary>
/// <param name="Code">The stable code, unique within the firm.</param>
/// <param name="Label">The label shown in the interface.</param>
/// <param name="Module">The module the entry belongs to.</param>
/// <param name="ParentId">The entry it sits beneath, or null for the top level.</param>
/// <param name="Route">The route it opens, or null for a heading.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
/// <param name="SortOrder">Its position among its siblings.</param>
public sealed record CreateMenuItemRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Label,
    [property: JsonRequired] string Module,
    Guid? ParentId = null,
    string? Route = null,
    string? LabelArabic = null,
    string? Icon = null,
    string? RequiredPermission = null,
    int SortOrder = 0);

/// <summary>Changing what an entry says and where it points.</summary>
/// <param name="Label">The new label.</param>
/// <param name="Route">The route it opens, or null for a heading.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Icon">The icon name.</param>
/// <param name="RequiredPermission">The permission needed to see it.</param>
/// <remarks>
/// Every field is applied, so an omitted one clears rather than preserves. The screen
/// sends the entry back as it should now read, which makes "clear the Arabic label"
/// expressible - a merge-style update never can.
/// </remarks>
public sealed record UpdateMenuItemRequest(
    [property: JsonRequired] string Label,
    string? Route = null,
    string? LabelArabic = null,
    string? Icon = null,
    string? RequiredPermission = null);

/// <summary>Moving an entry.</summary>
/// <param name="ParentId">The new parent, or null for the top level.</param>
/// <param name="SortOrder">The new position among its siblings.</param>
public sealed record MoveMenuItemRequest(
    Guid? ParentId,
    [property: JsonRequired] int SortOrder);

/// <summary>Showing or hiding an entry.</summary>
/// <param name="IsEnabled">Whether it should be shown.</param>
public sealed record SetMenuItemVisibilityRequest(
    [property: JsonRequired] bool IsEnabled);
