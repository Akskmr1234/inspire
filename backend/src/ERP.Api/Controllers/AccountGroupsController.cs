using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Account groups - the headings of the chart of accounts that ledgers sit under.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/account-groups")]
[Authorize]
[Produces("application/json")]
public sealed class AccountGroupsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="AccountGroupsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public AccountGroupsController(ISender sender) => _sender = sender;

    /// <summary>Lists the selected firm's account groups.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Every group, ordered by code.</returns>
    /// <response code="200">The groups.</response>
    [HttpGet]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<AccountGroupSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AccountGroupSummary>> result =
            await _sender.Send(new ListAccountGroupsQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Creates a group beneath another, taking its nature.</summary>
    /// <param name="request">The group.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The group as created.</returns>
    /// <response code="201">Created.</response>
    /// <response code="404">The parent is not in the selected firm.</response>
    /// <response code="409">The code is already used.</response>
    [HttpPost]
    [RequiresPermission("accounting", "ledger", "create")]
    [ProducesResponseType(typeof(AccountGroupSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateAccountGroupCommand request,
        CancellationToken cancellationToken)
    {
        Result<AccountGroupSummary> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.AccountGroupId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Renames a group.</summary>
    /// <param name="id">The group.</param>
    /// <param name="request">Its names.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The group as it now stands.</returns>
    /// <response code="200">Saved.</response>
    /// <response code="404">Not in the selected firm.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("accounting", "ledger", "edit")]
    [ProducesResponseType(typeof(AccountGroupSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] RenameAccountGroupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<AccountGroupSummary> result = await _sender.Send(
            new UpdateAccountGroupCommand(id, request.Name, request.NameArabic), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>A group's names.</summary>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
public sealed record RenameAccountGroupRequest(string Name, string? NameArabic = null);
