using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Users;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Users, and the roles they hold.</summary>
/// <remarks>
/// A user is created with a starting password that has to be changed at their first
/// sign-in, so the administrator who typed it is never left knowing a working one.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/platform/users")]
[Produces("application/json")]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserAdministration _administration;

    /// <summary>Initialises a new instance of the <see cref="UsersController"/> class.</summary>
    /// <param name="administration">User and role administration.</param>
    public UsersController(IUserAdministration administration) => _administration = administration;

    /// <summary>Lists the users.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every user.</returns>
    [HttpGet]
    [RequiresPermission("platform", "user", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await _administration.ListUsersAsync(cancellationToken));

    /// <summary>Creates a user.</summary>
    /// <param name="request">The user, with a starting password and their roles.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The user.</returns>
    [HttpPost]
    [RequiresPermission("platform", "user", "create")]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        Result<UserSummary> result = await _administration.CreateUserAsync(request, cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.UserId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a user: details, roles, and whether they may sign in.</summary>
    /// <param name="id">The user.</param>
    /// <param name="request">The user as they should be. The password is ignored.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The user.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "user", "edit")]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        Result<UserSummary> result = await _administration.UpdateUserAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Sets a new starting password, to be changed at the next sign-in.</summary>
    /// <param name="id">The user.</param>
    /// <param name="request">The new password.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    [HttpPost("{id:guid}/reset-password")]
    [RequiresPermission("platform", "user", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPasswordAsync(
        Guid id,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _administration.ResetPasswordAsync(id, request.Password, cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>A new starting password.</summary>
/// <param name="Password">The password, to be changed at the next sign-in.</param>
public sealed record ResetPasswordRequest([property: JsonRequired] string Password);
