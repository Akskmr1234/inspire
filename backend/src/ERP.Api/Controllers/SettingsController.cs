using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Settings;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The firm's named blocks of configuration, shared by every workstation.</summary>
/// <remarks>
/// Reading needs only a signed-in user of the firm: the entry screens consult these
/// settings to decide what to offer, and a cashier who could not read them would get a
/// screen configured for nobody. Changing them is an administrator's decision.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/settings")]
[Authorize]
[Produces("application/json")]
public sealed class SettingsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="SettingsController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public SettingsController(ISender sender) => _sender = sender;

    /// <summary>Reads one block of the firm's configuration.</summary>
    /// <param name="key">The block's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The block, with a null value where none has been saved.</returns>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(FirmSettingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(string key, CancellationToken cancellationToken)
    {
        Result<FirmSettingResponse> result = await _sender.Send(
            new GetFirmSettingQuery(key), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces one block of the firm's configuration.</summary>
    /// <param name="key">The block's name.</param>
    /// <param name="request">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpPut("{key}")]
    [RequiresPermission("platform", "setting", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveAsync(
        string key,
        [FromBody] SaveFirmSettingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SaveFirmSettingCommand(key, request.Value), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>A block of configuration to save.</summary>
/// <param name="Value">The document, as the client serialises it.</param>
public sealed record SaveFirmSettingRequest([property: JsonRequired] string Value);
