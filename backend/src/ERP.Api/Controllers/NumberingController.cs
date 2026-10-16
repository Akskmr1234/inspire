using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Platform.Numbering;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>How documents are numbered, and the number the next one will get.</summary>
/// <remarks>
/// Reading is open to any signed-in user, because every entry screen shows the number
/// its document will be given: a screen that invented one - "INV-1001" over a series
/// that would issue "SL/2026/0007" - was showing a number nobody would ever see again.
/// Changing the format is an administrator's decision.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/numbering")]
[Authorize]
[Produces("application/json")]
public sealed class NumberingController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="NumberingController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public NumberingController(ISender sender) => _sender = sender;

    /// <summary>Lists how each document a user enters is numbered.</summary>
    /// <param name="date">A date in the year to describe; today where omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One row per document type.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentNumberingView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DocumentNumberingView>> result = await _sender.Send(
            new ListDocumentNumberingQuery(date ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>The number the next document of a type would be given.</summary>
    /// <param name="documentType">The type, such as <c>sales.invoice</c>.</param>
    /// <param name="date">The document's date; today where omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next number, without reserving it.</returns>
    [HttpGet("{documentType}/next")]
    [ProducesResponseType(typeof(DocumentNumberingView), StatusCodes.Status200OK)]
    public async Task<IActionResult> PeekAsync(
        string documentType,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        Result<DocumentNumberingView> result = await _sender.Send(
            new PeekDocumentNumberQuery(
                documentType, date ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Changes how a document type is numbered from here on.</summary>
    /// <param name="documentType">The type.</param>
    /// <param name="request">The format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The format as it now stands.</returns>
    [HttpPut("{documentType}")]
    [RequiresPermission("platform", "numbering-series", "edit")]
    [ProducesResponseType(typeof(DocumentNumberingView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetAsync(
        string documentType,
        [FromBody] SetDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<DocumentNumberingView> result = await _sender.Send(
            new SetDocumentNumberingCommand(
                documentType,
                request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow),
                request.Prefix,
                request.Suffix,
                request.Separator ?? "/",
                request.IncludeYear,
                request.NextNumber),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>A numbering format to apply.</summary>
/// <param name="Prefix">Text before the number.</param>
/// <param name="Suffix">Text after the number.</param>
/// <param name="Separator">What joins the segments; a slash where omitted.</param>
/// <param name="IncludeYear">Whether the financial year appears in the number.</param>
/// <param name="NextNumber">The counter to continue from, or null to leave it.</param>
/// <param name="Date">A date in the year being configured; today where omitted.</param>
public sealed record SetDocumentNumberingRequest(
    string? Prefix,
    string? Suffix,
    string? Separator,
    [property: JsonRequired] bool IncludeYear,
    int? NextNumber = null,
    DateOnly? Date = null);
