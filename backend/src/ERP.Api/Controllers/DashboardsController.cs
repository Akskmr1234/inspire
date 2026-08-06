using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Dashboards;
using ERP.Domain.Platform;
using ERP.Identity.Authorization;
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

    /// <summary>Adds a panel driven by a query of the author's own.</summary>
    /// <param name="id">The dashboard.</param>
    /// <param name="request">The query and how to draw it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new panel's identifier.</returns>
    /// <remarks>
    /// The query must be a single <c>SELECT</c> or <c>WITH</c> returning columns named
    /// <c>label</c> and <c>value</c>. It runs inside a read-only transaction, under a
    /// statement timeout and a row cap, as the ordinary application role - so
    /// row-level security applies to it exactly as to every other query and it cannot
    /// reach another tenant's rows.
    /// <para>
    /// Authoring one requires <c>reporting:dashboard:create</c>, and so does reading
    /// the panel afterwards: a statement somebody wrote can reach far more than any
    /// single report exposes, and merely being given a dashboard is not consent to
    /// that.
    /// </para>
    /// </remarks>
    /// <response code="201">Added.</response>
    /// <response code="400">The query was refused, or the panel is invalid.</response>
    /// <response code="404">No such dashboard in the selected firm.</response>
    [HttpPost("{id:guid}/widgets")]
    [RequiresPermission("reporting", "dashboard", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddCustomWidgetAsync(
        Guid id,
        [FromBody] AddCustomWidgetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Guid> result = await _sender.Send(
            new AddCustomWidgetCommand(
                id, request.Query, request.Title, request.Kind,
                request.SortOrder, request.Span),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Removes a panel from a dashboard.</summary>
    /// <param name="id">The dashboard.</param>
    /// <param name="widgetId">The panel.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Removed.</response>
    /// <response code="404">No such dashboard or panel.</response>
    [HttpDelete("{id:guid}/widgets/{widgetId:guid}")]
    [RequiresPermission("reporting", "dashboard", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveWidgetAsync(
        Guid id,
        Guid widgetId,
        CancellationToken cancellationToken)
    {
        Result result = await _sender.Send(
            new RemoveWidgetCommand(id, widgetId), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}

/// <summary>Adding a panel driven by a query of the author's own.</summary>
/// <param name="Query">
/// A single SELECT or WITH returning columns named <c>label</c> and <c>value</c>.
/// </param>
/// <param name="Title">The heading shown on the panel.</param>
/// <param name="Kind">How the result is drawn.</param>
/// <param name="SortOrder">The position among the dashboard's panels.</param>
/// <param name="Span">How many grid columns it occupies.</param>
public sealed record AddCustomWidgetRequest(
    [property: JsonRequired] string Query,
    [property: JsonRequired] string Title,
    [property: JsonRequired] WidgetKind Kind,
    int SortOrder = 0,
    int Span = 1);
