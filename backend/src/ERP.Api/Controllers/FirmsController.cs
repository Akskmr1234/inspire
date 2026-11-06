using Asp.Versioning;
using ERP.Application.Platform.Firms;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The tenant's firms: listing, adding and correcting them.</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/platform/firms")]
[Produces("application/json")]
public sealed class FirmsController : ApiControllerBase
{
    private readonly IFirmAdministration _firms;

    /// <summary>Initialises a new instance of the <see cref="FirmsController"/> class.</summary>
    /// <param name="firms">The firm administration.</param>
    public FirmsController(IFirmAdministration firms) => _firms = firms;

    /// <summary>Lists every firm with its branches, for the screens that manage them.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firms.</returns>
    [HttpGet]
    [RequiresPermission("platform", "user", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<FirmSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await _firms.ListAsync(cancellationToken));

    /// <summary>Lists the firms and branches the caller may work in, for the firm switcher.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The caller's firms.</returns>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<FirmSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken) =>
        Ok(await _firms.ListMineAsync(cancellationToken));

    /// <summary>Adds a firm, set up with a head office, chart of accounts and open year.</summary>
    /// <param name="request">The firm to add.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firm as added.</returns>
    [HttpPost]
    [RequiresPermission("platform", "firm", "create")]
    [ProducesResponseType(typeof(FirmSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveFirmRequest request,
        CancellationToken cancellationToken)
    {
        Result<FirmSummary> result = await _firms.CreateAsync(request, cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.FirmId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a firm.</summary>
    /// <param name="id">The firm.</param>
    /// <param name="request">The firm as it should be. Its code and currency stay as they were.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firm as it now stands.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(FirmSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveFirmRequest request,
        CancellationToken cancellationToken)
    {
        Result<FirmSummary> result = await _firms.UpdateAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
