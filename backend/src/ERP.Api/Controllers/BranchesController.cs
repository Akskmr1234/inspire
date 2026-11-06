using Asp.Versioning;
using ERP.Application.Platform.Branches;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The firm's branches: listing, adding and correcting them.</summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/platform/branches")]
[Produces("application/json")]
public sealed class BranchesController : ApiControllerBase
{
    private readonly IBranchAdministration _branches;

    /// <summary>Initialises a new instance of the <see cref="BranchesController"/> class.</summary>
    /// <param name="branches">The branch administration.</param>
    public BranchesController(IBranchAdministration branches) => _branches = branches;

    /// <summary>Lists every branch of the firm, for the screens that manage them.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branches.</returns>
    [HttpGet]
    [RequiresPermission("platform", "user", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<BranchSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await _branches.ListAsync(cancellationToken));

    /// <summary>Lists the branches the caller may work in, for the branch switcher.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The caller's branches.</returns>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<BranchSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken) =>
        Ok(await _branches.ListMineAsync(cancellationToken));

    /// <summary>Adds a branch.</summary>
    /// <param name="request">The branch to add.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branch as added.</returns>
    [HttpPost]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(BranchSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveBranchRequest request,
        CancellationToken cancellationToken)
    {
        Result<BranchSummary> result = await _branches.CreateAsync(request, cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.BranchId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a branch.</summary>
    /// <param name="id">The branch.</param>
    /// <param name="request">The branch as it should be. Its code stays as it was.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branch as it now stands.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(BranchSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveBranchRequest request,
        CancellationToken cancellationToken)
    {
        Result<BranchSummary> result = await _branches.UpdateAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
