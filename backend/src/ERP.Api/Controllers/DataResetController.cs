using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Data;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Clears a firm's data, for a new customer or a fresh test.</summary>
/// <remarks>
/// Behind the permission to delete the firm itself, which is the nearest thing the
/// catalogue has to what this does, and confirmed by the firm's name in the request.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/platform/data")]
[Produces("application/json")]
public sealed class DataResetController : ApiControllerBase
{
    private readonly IDataReset _reset;

    /// <summary>Initialises a new instance of the <see cref="DataResetController"/> class.</summary>
    /// <param name="reset">The reset.</param>
    public DataResetController(IDataReset reset) => _reset = reset;

    /// <summary>Counts what a reset would remove, and names the firm to confirm with.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The counts.</returns>
    [HttpGet]
    [RequiresPermission("platform", "firm", "delete")]
    [ProducesResponseType(typeof(DataResetPreview), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewAsync(CancellationToken cancellationToken)
    {
        Result<DataResetPreview> result = await _reset.PreviewAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Clears the selected firm's transactions, or its transactions and masters.</summary>
    /// <param name="request">What to clear, and the firm's name to confirm it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was removed.</returns>
    /// <response code="200">Cleared.</response>
    /// <response code="400">Not confirmed: nothing was cleared.</response>
    [HttpPost("reset")]
    [RequiresPermission("platform", "firm", "delete")]
    [ProducesResponseType(typeof(DataResetResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetAsync(
        [FromBody] DataResetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<DataResetResult> result = await _reset.ResetAsync(
            request.Scope, request.Confirmation, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>What to clear.</summary>
/// <param name="Scope">Transactions only, or transactions and masters.</param>
/// <param name="Confirmation">The firm's name or code.</param>
public sealed record DataResetRequest(
    [property: JsonRequired] DataResetScope Scope,
    [property: JsonRequired] string Confirmation);
