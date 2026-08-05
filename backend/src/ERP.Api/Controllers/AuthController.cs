using System.Security.Claims;
using Asp.Versioning;
using ERP.Application.Abstractions.Security;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Sign-in, token renewal, sign-out, and password change.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authentication;

    /// <summary>Initialises a new instance of the <see cref="AuthController"/> class.</summary>
    /// <param name="authentication">The authentication service.</param>
    public AuthController(IAuthenticationService authentication) =>
        _authentication = authentication;

    /// <summary>Signs in with a user name or email address and a password.</summary>
    /// <param name="request">The credentials.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An access token, a refresh token, and when the access token expires.</returns>
    /// <response code="200">Signed in.</response>
    /// <response code="401">
    /// The credentials were rejected. Deliberately the same response for an unknown
    /// user, a wrong password, a disabled account, and a locked one.
    /// </response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<AuthenticationResponse> result = await _authentication.SignInAsync(
            new SignInRequest(
                request.UserName,
                request.Password,
                Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Exchanges a refresh token for a new token pair.</summary>
    /// <param name="request">The refresh token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A new access token and refresh token.</returns>
    /// <response code="200">Renewed.</response>
    /// <response code="401">
    /// The refresh token was unusable. Note that presenting an already-used token
    /// revokes the entire session family, because reuse cannot be distinguished
    /// from theft.
    /// </response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<AuthenticationResponse> result = await _authentication.RefreshAsync(
            new RefreshRequest(
                request.RefreshToken,
                Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Signs out, ending the session.</summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">
    /// Signed out. Returned even for an unrecognised token: the desired state
    /// already holds, and distinguishing the cases would reveal whether a given
    /// token ever existed.
    /// </response>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAsync(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _authentication.SignOutAsync(request.RefreshToken, cancellationToken);

        return NoContent();
    }

    /// <summary>Changes the signed-in user's password.</summary>
    /// <param name="request">The current and replacement passwords.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Changed. Every other session for this user is ended.</response>
    /// <response code="400">The current password was wrong, or the new one fails policy.</response>
    /// <response code="401">Not signed in.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out Guid userId))
        {
            return Unauthorized();
        }

        Result result = await _authentication.ChangePasswordAsync(
            UserId.From(userId), request.CurrentPassword, request.NewPassword, cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Returns the signed-in user's effective permissions.</summary>
    /// <param name="permissionChecker">The permission checker.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every permission code the caller holds.</returns>
    /// <remarks>
    /// The client uses this to build the menu and to enable or disable actions, so
    /// a user is not offered buttons that will refuse them. It is not a security
    /// boundary - every endpoint still checks for itself.
    /// </remarks>
    /// <response code="200">The permission codes.</response>
    [HttpGet("permissions")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyCollection<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionsAsync(
        [FromServices] IPermissionChecker permissionChecker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissionChecker);

        string? subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out Guid userId))
        {
            return Unauthorized();
        }

        IReadOnlySet<string> permissions = await permissionChecker.GetPermissionsAsync(
            UserId.From(userId), cancellationToken);

        return Ok(permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Translates a domain error into an RFC 9457 problem response.
    /// </summary>
    /// <param name="error">The failure.</param>
    /// <returns>The problem response.</returns>
    /// <remarks>
    /// The single place error kinds become HTTP status codes, so the mapping
    /// cannot drift between endpoints.
    /// </remarks>
    private ObjectResult Problem(Error error)
    {
        int status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Problem(
            detail: error.Description,
            statusCode: status,
            title: error.Code);
    }
}

/// <summary>Credentials for signing in.</summary>
/// <param name="UserName">The sign-in name or email address.</param>
/// <param name="Password">The password.</param>
public sealed record LoginRequest(string UserName, string Password);

/// <summary>A refresh token.</summary>
/// <param name="RefreshToken">The token issued previously.</param>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>A password change.</summary>
/// <param name="CurrentPassword">The existing password.</param>
/// <param name="NewPassword">The replacement.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
