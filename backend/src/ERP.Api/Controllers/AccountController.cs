using System.Security.Claims;
using Asp.Versioning;
using ERP.Application.Platform.Users;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The signed-in user's own account: their details and where they are signed in.</summary>
/// <remarks>
/// Needs nothing but a sign-in. Every action reads the user from their own token, so
/// there is no identifier in the route that could be pointed at somebody else.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/account")]
[Produces("application/json")]
[Authorize]
public sealed class AccountController : ApiControllerBase
{
    /// <summary>The claim naming the sign-in a token belongs to.</summary>
    private const string SessionClaim = "sid";

    private readonly IMyAccount _account;

    /// <summary>Initialises a new instance of the <see cref="AccountController"/> class.</summary>
    /// <param name="account">The account service.</param>
    public AccountController(IMyAccount account) => _account = account;

    /// <summary>Reads the signed-in user's account.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The account, with its roles and where it is signed in.</returns>
    /// <response code="200">The account.</response>
    /// <response code="401">Not signed in.</response>
    [HttpGet]
    [ProducesResponseType(typeof(MyAccountResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        MyAccountResponse? account = await _account.GetAsync(
            userId, CallerSession(), cancellationToken);

        return account is null ? Unauthorized() : Ok(account);
    }

    /// <summary>Corrects the signed-in user's name, email and mobile number.</summary>
    /// <param name="request">The details as they should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The account as it now stands.</returns>
    /// <response code="200">Saved.</response>
    /// <response code="400">A detail is missing or malformed.</response>
    /// <response code="409">The email address belongs to another user.</response>
    [HttpPut]
    [ProducesResponseType(typeof(MyAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        [FromBody] UpdateMyAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        Result<MyAccountResponse> result = await _account.UpdateAsync(
            userId, CallerSession(), request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Signs the user out of one place.</summary>
    /// <param name="sessionId">The sign-in to end.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Ended. That device has to sign in again when its access runs out.</response>
    /// <response code="404">Not one of the user's sign-ins, or already ended.</response>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        Result result = await _account.EndSessionAsync(userId, sessionId, cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Signs the user out everywhere but here.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>How many sign-ins were ended.</returns>
    /// <response code="200">Ended.</response>
    [HttpPost("sessions/sign-out-others")]
    [ProducesResponseType(typeof(SignedOutSessions), StatusCodes.Status200OK)]
    public async Task<IActionResult> EndOtherSessionsAsync(CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        int ended = await _account.EndOtherSessionsAsync(
            userId, CallerSession(), cancellationToken);

        return Ok(new SignedOutSessions(ended));
    }

    private UserId? CallerId()
    {
        string? subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(subject, out Guid id) ? UserId.From(id) : null;
    }

    private Guid? CallerSession() =>
        Guid.TryParse(User.FindFirstValue(SessionClaim), out Guid id) ? id : null;
}

/// <summary>How many sign-ins were ended.</summary>
/// <param name="Ended">The number of devices signed out.</param>
public sealed record SignedOutSessions(int Ended);
