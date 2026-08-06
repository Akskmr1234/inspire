using Asp.Versioning;
using ERP.Application.Platform.Dashboards;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Dashboards, and the figures on them.</summary>
/// <remarks>
/// A dashboard is assigned to roles rather than gated by a permission of its own, so
/// what comes back is what the caller was given to look at. The figures inside it are
/// a separate question: each metric answers to the permission of the report behind it,
/// and one the caller cannot read comes back marked withheld rather than as a zero.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboards")]
[Authorize]
[Produces("application/json")]
public sealed class DashboardsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="DashboardsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public DashboardsController(ISender sender) => _sender = sender;

    /// <summary>Lists the dashboards the caller has been given.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The dashboards with their panels, in display order.</returns>
    /// <remarks>
    /// A dashboard assigned to two of the caller's roles appears once. Overlapping
    /// audiences are the ordinary case rather than a mistake.
    /// </remarks>
    /// <response code="200">The dashboards.</response>
    /// <response code="403">No firm is selected, or no user is signed in.</response>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        Result<DashboardsResponse> result = await _sender.Send(
            new GetDashboardsQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Computes the figures for one dashboard.</summary>
    /// <param name="id">The dashboard.</param>
    /// <param name="asAt">The date to state the figures as at. Defaults to today.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One entry per distinct metric on the dashboard.</returns>
    /// <remarks>
    /// Every figure here also appears on a report that can be opened in full, and is
    /// computed by the same reader - a headline that disagreed with the report behind
    /// it would be worse than no headline.
    /// <para>
    /// A metric the caller may not read is returned with <c>isPermitted</c> false so
    /// the panel can say so, rather than drawing a confident zero that reads as
    /// "nothing is owed".
    /// </para>
    /// </remarks>
    /// <response code="200">The figures.</response>
    /// <response code="404">No such dashboard is available to the caller.</response>
    [HttpGet("{id:guid}/data")]
    [ProducesResponseType(typeof(DashboardDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDataAsync(
        Guid id,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<DashboardDataResponse> result = await _sender.Send(
            new GetDashboardDataQuery(id, asAt), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
