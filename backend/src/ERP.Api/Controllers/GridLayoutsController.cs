using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Grids;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>How the signed-in user has arranged their data grids.</summary>
/// <remarks>
/// Behind no permission beyond being signed in, deliberately. A layout is personal:
/// it is read only by the user who wrote it and affects nothing anybody else sees, so
/// gating it would mean an administrator granting people the right to arrange their
/// own screens.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/grid-layouts")]
[Authorize]
[Produces("application/json")]
public sealed class GridLayoutsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="GridLayoutsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public GridLayoutsController(ISender sender) => _sender = sender;

    /// <summary>Returns the caller's arrangement for one grid.</summary>
    /// <param name="gridKey">The grid, for example <c>ledgers</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The saved arrangement, or a null state when none has been saved.</returns>
    /// <remarks>
    /// Never having saved one is the ordinary case rather than an error, so this
    /// answers 200 with a null <c>state</c> and the client falls back to the grid's
    /// own defaults.
    /// </remarks>
    /// <response code="200">The arrangement, or null.</response>
    [HttpGet("{gridKey}")]
    [ProducesResponseType(typeof(GridLayoutResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        string gridKey,
        CancellationToken cancellationToken)
    {
        Result<GridLayoutResponse> result = await _sender.Send(
            new GetGridLayoutQuery(gridKey), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Records the caller's arrangement for one grid.</summary>
    /// <param name="gridKey">The grid.</param>
    /// <param name="request">The arrangement.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// An upsert: the caller is arranging a grid, and whether they have done so before
    /// is not something a screen should have to track.
    /// </remarks>
    /// <response code="204">Saved.</response>
    /// <response code="400">The arrangement is missing or too large.</response>
    [HttpPut("{gridKey}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveAsync(
        string gridKey,
        [FromBody] SaveGridLayoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SaveGridLayoutCommand(gridKey, request.State), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Forgets the caller's arrangement, returning the grid to its default.</summary>
    /// <param name="gridKey">The grid.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <remarks>
    /// Resetting a grid that was never customised succeeds: it is what the caller
    /// asked for and already true.
    /// </remarks>
    /// <response code="204">Reset.</response>
    [HttpDelete("{gridKey}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetAsync(
        string gridKey,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new ResetGridLayoutCommand(gridKey), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Saving a grid arrangement.</summary>
/// <param name="State">
/// The arrangement, as a JSON document. Opaque to the server, which stores it and
/// hands it back to the client that wrote it.
/// </param>
public sealed record SaveGridLayoutRequest([property: JsonRequired] string State);
