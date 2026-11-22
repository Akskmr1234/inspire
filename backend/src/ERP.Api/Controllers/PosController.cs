using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Sales;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The point of sale: a cashier's shift, and the close of the trading day.</summary>
/// <remarks>
/// The sales themselves are ordinary sales invoices and returns, raised through the sales
/// endpoints; these only bracket them, so a shift and a day can say what they took.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sales/pos")]
[Authorize]
[Produces("application/json")]
public sealed class PosController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="PosController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public PosController(ISender sender) => _sender = sender;

    /// <summary>Where the counter stands for the signed-in cashier.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Today, the cashier's open shift, and whether the day is closed.</returns>
    /// <response code="200">The status.</response>
    [HttpGet("status")]
    [RequiresPermission("sales", "pos", "view")]
    [ProducesResponseType(typeof(PosStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        Result<PosStatusResponse> result = await _sender.Send(new GetPosStatusQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Opens the signed-in cashier's shift.</summary>
    /// <param name="request">The float in the drawer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The shift.</returns>
    /// <response code="200">Opened.</response>
    /// <response code="409">A shift of theirs is already open.</response>
    /// <response code="422">The day is closed.</response>
    [HttpPost("shifts")]
    [RequiresPermission("sales", "pos", "create")]
    [ProducesResponseType(typeof(PosShiftView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> OpenShiftAsync(
        [FromBody] OpenPosShiftRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PosShiftView> result = await _sender.Send(
            new OpenPosShiftCommand(request.OpeningCash), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Closes the cashier's shift with the cash counted.</summary>
    /// <param name="id">The shift.</param>
    /// <param name="request">The cash counted, and a note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The closed shift, with what it took and what the drawer should have held.</returns>
    /// <response code="200">Closed.</response>
    /// <response code="404">No such open shift of the cashier's.</response>
    [HttpPost("shifts/{id:guid}/close")]
    [RequiresPermission("sales", "pos", "create")]
    [ProducesResponseType(typeof(PosShiftView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseShiftAsync(
        Guid id,
        [FromBody] ClosePosShiftRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PosShiftView> result = await _sender.Send(
            new ClosePosShiftCommand(id, request.CountedCash, request.Notes), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>A day's takings, and whether it is closed.</summary>
    /// <param name="date">The trading day to read; today when omitted.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The takings and the state of the day.</returns>
    /// <response code="200">The day.</response>
    [HttpGet("day")]
    [RequiresPermission("sales", "pos", "view")]
    [ProducesResponseType(typeof(PosDayView), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDayAsync(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        Result<PosDayView> result = await _sender.Send(new GetPosDayQuery(date), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Closes a trading day.</summary>
    /// <param name="request">The day and a note.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The closed day with its takings.</returns>
    /// <response code="200">Closed.</response>
    /// <response code="409">Already closed.</response>
    /// <response code="422">A shift is still open.</response>
    [HttpPost("day/close")]
    [RequiresPermission("sales", "pos", "approve")]
    [ProducesResponseType(typeof(PosDayView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseDayAsync(
        [FromBody] ClosePosDayRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<PosDayView> result = await _sender.Send(
            new ClosePosDayCommand(request.Date, request.Notes), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>Opening a shift.</summary>
/// <param name="OpeningCash">The float in the drawer.</param>
public sealed record OpenPosShiftRequest([property: JsonRequired] decimal OpeningCash);

/// <summary>Closing a shift.</summary>
/// <param name="CountedCash">The cash counted in the drawer.</param>
/// <param name="Notes">Anything to say about a difference.</param>
public sealed record ClosePosShiftRequest([property: JsonRequired] decimal CountedCash, string? Notes = null);

/// <summary>Closing a day.</summary>
/// <param name="Date">The day; today when omitted.</param>
/// <param name="Notes">Anything the manager wants to say.</param>
public sealed record ClosePosDayRequest(DateOnly? Date = null, string? Notes = null);
