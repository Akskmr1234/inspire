using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Parties;
using ERP.Domain.Parties;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Where a firm's parties are: countries with their currencies, states, areas and routes.</summary>
/// <remarks>
/// Kept with the customer permissions: they are the lists a customer's address is chosen
/// from, and whoever may keep the customer list may keep these.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/parties")]
[Authorize]
[Produces("application/json")]
public sealed class PlacesController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="PlacesController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public PlacesController(ISender sender) => _sender = sender;

    /// <summary>Lists the countries.</summary>
    /// <param name="includeInactive">Whether withdrawn countries are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The countries, by name.</returns>
    /// <response code="200">The countries.</response>
    [HttpGet("countries")]
    [RequiresPermission("sales", "customer", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<CountrySummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCountriesAsync(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Reply(await _sender.Send(new ListCountriesQuery(includeInactive), cancellationToken));

    /// <summary>Adds a country.</summary>
    /// <param name="request">The country.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new country's identifier.</returns>
    /// <response code="201">Added.</response>
    /// <response code="400">The country is invalid.</response>
    /// <response code="409">The code is already a country.</response>
    [HttpPost("countries")]
    [RequiresPermission("sales", "customer", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCountryAsync(
        [FromBody] CreateCountryCommand request,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await _sender.Send(request, cancellationToken));

    /// <summary>Corrects a country's names and currency.</summary>
    /// <param name="id">The country.</param>
    /// <param name="request">The country as it should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Changed.</response>
    /// <response code="400">The change is invalid.</response>
    /// <response code="404">No such country.</response>
    [HttpPut("countries/{id:guid}")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCountryAsync(
        Guid id,
        [FromBody] UpdateCountryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(request with { Id = id }, cancellationToken));
    }

    /// <summary>Withdraws a country from use, or puts it back.</summary>
    /// <param name="id">The country.</param>
    /// <param name="request">Whether it should be offered.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Changed.</response>
    /// <response code="404">No such country.</response>
    [HttpPut("countries/{id:guid}/active")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCountryActiveAsync(
        Guid id,
        [FromBody] PlaceActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(new SetCountryActiveCommand(id, request.IsActive), cancellationToken));
    }

    /// <summary>Lists the states, of one country or all.</summary>
    /// <param name="countryId">One country. Omit for all.</param>
    /// <param name="includeInactive">Whether withdrawn states are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The states, by name.</returns>
    /// <response code="200">The states.</response>
    [HttpGet("states")]
    [RequiresPermission("sales", "customer", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<StateSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStatesAsync(
        [FromQuery] Guid? countryId,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Reply(await _sender.Send(new ListStatesQuery(countryId, includeInactive), cancellationToken));

    /// <summary>Adds a state to a country.</summary>
    /// <param name="request">The state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new state's identifier.</returns>
    /// <response code="201">Added.</response>
    /// <response code="400">The state is invalid.</response>
    /// <response code="404">No such country.</response>
    /// <response code="409">The code is already a state's.</response>
    [HttpPost("states")]
    [RequiresPermission("sales", "customer", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateStateAsync(
        [FromBody] CreateStateCommand request,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await _sender.Send(request, cancellationToken));

    /// <summary>Corrects a state, or moves it to another country.</summary>
    /// <param name="id">The state.</param>
    /// <param name="request">The state as it should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Changed.</response>
    /// <response code="400">The change is invalid.</response>
    /// <response code="404">No such state or country.</response>
    [HttpPut("states/{id:guid}")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStateAsync(
        Guid id,
        [FromBody] UpdateStateCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(request with { Id = id }, cancellationToken));
    }

    /// <summary>Withdraws a state from use, or puts it back.</summary>
    /// <param name="id">The state.</param>
    /// <param name="request">Whether it should be offered.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Changed.</response>
    /// <response code="404">No such state.</response>
    [HttpPut("states/{id:guid}/active")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStateActiveAsync(
        Guid id,
        [FromBody] PlaceActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(new SetStateActiveCommand(id, request.IsActive), cancellationToken));
    }

    /// <summary>Lists the areas and routes.</summary>
    /// <param name="kind">Areas or routes. Omit for both.</param>
    /// <param name="includeInactive">Whether withdrawn ones are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The areas and routes, by name.</returns>
    /// <response code="200">The areas and routes.</response>
    [HttpGet("territories")]
    [RequiresPermission("sales", "customer", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<TerritorySummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTerritoriesAsync(
        [FromQuery] TerritoryKind? kind,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Reply(await _sender.Send(new ListTerritoriesQuery(kind, includeInactive), cancellationToken));

    /// <summary>Adds an area or a route.</summary>
    /// <param name="request">The area or route.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new identifier.</returns>
    /// <response code="201">Added.</response>
    /// <response code="400">It is invalid.</response>
    /// <response code="409">There is already one of that name.</response>
    [HttpPost("territories")]
    [RequiresPermission("sales", "customer", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTerritoryAsync(
        [FromBody] CreateTerritoryCommand request,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await _sender.Send(request, cancellationToken));

    /// <summary>Renames an area or a route.</summary>
    /// <param name="id">The area or route.</param>
    /// <param name="request">Its new name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Renamed.</response>
    /// <response code="400">The name is invalid.</response>
    /// <response code="404">No such area or route.</response>
    [HttpPut("territories/{id:guid}")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameTerritoryAsync(
        Guid id,
        [FromBody] RenameTerritoryCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(request with { Id = id }, cancellationToken));
    }

    /// <summary>Withdraws an area or a route from use, or puts it back.</summary>
    /// <param name="id">The area or route.</param>
    /// <param name="request">Whether it should be offered.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Changed.</response>
    /// <response code="404">No such area or route.</response>
    [HttpPut("territories/{id:guid}/active")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTerritoryActiveAsync(
        Guid id,
        [FromBody] PlaceActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Reply(await _sender.Send(new SetTerritoryActiveCommand(id, request.IsActive), cancellationToken));
    }

    private IActionResult Reply<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error);

    private IActionResult Reply(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error);

    private ObjectResult CreatedOrProblem(Result<Guid> result) =>
        result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error);
}

/// <summary>Whether a country, state, area or route should be offered.</summary>
/// <param name="IsActive">True to offer it again, false to withdraw it.</param>
public sealed record PlaceActiveRequest([property: JsonRequired] bool IsActive);
