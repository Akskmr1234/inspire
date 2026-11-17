using System.Security.Claims;
using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Abstractions.Security;
using ERP.Domain.Identity;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The signed-in user's own second factors.</summary>
/// <remarks>
/// Every action reads the user from their own token, so nobody can add or remove a
/// factor on somebody else's account here. An administrator's reset is on the users
/// screen instead.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/account/mfa")]
[Produces("application/json")]
[Authorize]
public sealed class MfaController : ApiControllerBase
{
    private readonly IMfaAdministration _mfa;

    /// <summary>Initialises a new instance of the <see cref="MfaController"/> class.</summary>
    /// <param name="mfa">The second-factor administration.</param>
    public MfaController(IMfaAdministration mfa) => _mfa = mfa;

    /// <summary>Lists the user's second factors.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The factors, and which kinds this server offers.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(MfaOverview), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        CallerId() is { } userId
            ? Ok(await _mfa.ListAsync(userId, cancellationToken))
            : Unauthorized();

    /// <summary>Starts adding a second factor.</summary>
    /// <param name="request">What kind, and a name for it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The QR code to scan, or the backup codes.</returns>
    /// <response code="200">Started.</response>
    /// <response code="422">This server does not offer that kind.</response>
    [HttpPost]
    [ProducesResponseType(typeof(MfaEnrolmentStarted), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BeginAsync(
        [FromBody] MfaBeginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        Result<MfaEnrolmentStarted> result = await _mfa.BeginAsync(
            userId, request.Kind, request.Label, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Finishes adding a factor: a first code, or that the phone registered.</summary>
    /// <param name="id">The factor.</param>
    /// <param name="request">The first code, for an authenticator.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Whether it is now in use.</returns>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(typeof(MfaConfirmResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmAsync(
        Guid id,
        [FromBody] MfaConfirmRequest? request,
        CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        Result<bool> result = await _mfa.ConfirmAsync(userId, id, request?.Code, cancellationToken);

        return result.IsSuccess ? Ok(new MfaConfirmResponse(result.Value)) : Problem(result.Error);
    }

    /// <summary>Removes one of the user's second factors.</summary>
    /// <param name="id">The factor.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (CallerId() is not { } userId)
        {
            return Unauthorized();
        }

        Result result = await _mfa.RemoveAsync(userId, id, cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    private UserId? CallerId()
    {
        string? subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(subject, out Guid id) ? UserId.From(id) : null;
    }
}

/// <summary>A second factor to start adding.</summary>
/// <param name="Kind">What kind.</param>
/// <param name="Label">A name for it, such as "Work phone".</param>
public sealed record MfaBeginRequest([property: JsonRequired] MfaFactorKind Kind, string? Label = null);

/// <summary>The first code from a new authenticator.</summary>
/// <param name="Code">The code.</param>
public sealed record MfaConfirmRequest(string? Code);

/// <summary>Whether a factor is now in use.</summary>
/// <param name="Active">True once sign-in asks for it.</param>
public sealed record MfaConfirmResponse(bool Active);
