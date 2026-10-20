using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Settings;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The firm's financial years: the periods every document posts into.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/financial-years")]
[Authorize]
[Produces("application/json")]
public sealed class FinancialYearsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="FinancialYearsController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public FinancialYearsController(ISender sender) => _sender = sender;

    /// <summary>Lists the firm's financial years, earliest first.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The years.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FinancialYearSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<FinancialYearSummary>> result = await _sender.Send(
            new ListFinancialYearsQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Adds a financial year.</summary>
    /// <param name="request">Its label and dates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year as created.</returns>
    /// <response code="201">The year.</response>
    /// <response code="409">It overlaps a year the firm already has, or repeats its label.</response>
    [HttpPost]
    [RequiresPermission("platform", "setting", "edit")]
    [ProducesResponseType(typeof(FinancialYearSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] FinancialYearRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FinancialYearSummary> result = await _sender.Send(
            new CreateFinancialYearCommand(request.Code, request.StartDate, request.EndDate),
            cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(ListAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Renames a financial year or moves its dates.</summary>
    /// <param name="id">The year's identifier.</param>
    /// <param name="request">Its label and dates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year with its new label and dates.</returns>
    /// <response code="200">The year.</response>
    /// <response code="422">A document posted into it would fall outside the new dates.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "setting", "edit")]
    [ProducesResponseType(typeof(FinancialYearSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] FinancialYearRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FinancialYearSummary> result = await _sender.Send(
            new UpdateFinancialYearCommand(id, request.Code, request.StartDate, request.EndDate),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Closes a year to postings, or opens it again.</summary>
    /// <param name="id">The year's identifier.</param>
    /// <param name="request">Whether it should take postings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year, open or closed.</returns>
    [HttpPost("{id:guid}/open")]
    [RequiresPermission("platform", "setting", "edit")]
    [ProducesResponseType(typeof(FinancialYearSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetOpenAsync(
        Guid id,
        [FromBody] FinancialYearOpenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<FinancialYearSummary> result = await _sender.Send(
            new SetFinancialYearOpenCommand(id, request.Open), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>A financial year's label and dates.</summary>
/// <param name="Code">Its label, such as <c>2026</c> or <c>2026-27</c>.</param>
/// <param name="StartDate">The first date, inclusive.</param>
/// <param name="EndDate">The last date, inclusive.</param>
public sealed record FinancialYearRequest(
    [property: JsonRequired] string Code,
    [property: JsonRequired] DateOnly StartDate,
    [property: JsonRequired] DateOnly EndDate);

/// <summary>Whether a year should take postings.</summary>
/// <param name="Open">True to open it, false to close it.</param>
public sealed record FinancialYearOpenRequest([property: JsonRequired] bool Open);
