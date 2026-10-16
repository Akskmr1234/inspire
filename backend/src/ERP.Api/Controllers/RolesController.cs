using Asp.Versioning;
using ERP.Application.Platform.Users;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Roles: what each may do, and which menus it shows.</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/platform/roles")]
[Produces("application/json")]
public sealed class RolesController : ApiControllerBase
{
    private readonly IUserAdministration _administration;

    /// <summary>Initialises a new instance of the <see cref="RolesController"/> class.</summary>
    /// <param name="administration">User and role administration.</param>
    public RolesController(IUserAdministration administration) => _administration = administration;

    /// <summary>Lists the roles.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every role, with its permissions and menus.</returns>
    [HttpGet]
    [RequiresPermission("platform", "role", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<RoleSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await _administration.ListRolesAsync(cancellationToken));

    /// <summary>Lists every permission a role can grant.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The permission catalogue.</returns>
    [HttpGet("permissions")]
    [RequiresPermission("platform", "role", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDefinition>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPermissionsAsync(CancellationToken cancellationToken) =>
        Ok(await _administration.ListPermissionsAsync(cancellationToken));

    /// <summary>Creates a role.</summary>
    /// <param name="request">The role: its permissions, and the menus it shows.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The role.</returns>
    [HttpPost]
    [RequiresPermission("platform", "role", "create")]
    [ProducesResponseType(typeof(RoleSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        Result<RoleSummary> result = await _administration.CreateRoleAsync(request, cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.RoleId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a role.</summary>
    /// <param name="id">The role.</param>
    /// <param name="request">The role as it should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The role as it now stands.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "role", "edit")]
    [ProducesResponseType(typeof(RoleSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        Result<RoleSummary> result = await _administration.UpdateRoleAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Deletes a role nobody holds.</summary>
    /// <param name="id">The role.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    [HttpDelete("{id:guid}")]
    [RequiresPermission("platform", "role", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        Result result = await _administration.DeleteRoleAsync(id, cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}
