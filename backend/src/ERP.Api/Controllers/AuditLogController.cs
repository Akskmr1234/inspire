using Asp.Versioning;
using ERP.Application.Platform.Audit;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The audit trail: who created, changed or deleted which master or transaction, and how.</summary>
/// <remarks>Read-only. Nothing here, or anywhere else in the API, changes or removes an entry.</remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/audit-log")]
[Authorize]
[Produces("application/json")]
public sealed class AuditLogController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="AuditLogController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public AuditLogController(ISender sender) => _sender = sender;

    /// <summary>Searches the trail, newest first.</summary>
    /// <param name="module">Only changes to records of this module.</param>
    /// <param name="userId">Only changes made by this user.</param>
    /// <param name="action">Only Create, Update or Delete.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="search">Text in the record's number, code or name, or the user's name.</param>
    /// <param name="page">The page, from one.</param>
    /// <param name="pageSize">How many entries a page holds, at most 200.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One page of entries.</returns>
    [HttpGet]
    [RequiresPermission("platform", "audit-log", "view")]
    [ProducesResponseType(typeof(AuditLogPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? module,
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        Result<AuditLogPage> result = await _sender.Send(
            new ListAuditLogQuery(module, userId, action, from, to, search, page, pageSize),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Lists the modules and users the trail can be filtered by.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The filters.</returns>
    [HttpGet("filters")]
    [RequiresPermission("platform", "audit-log", "view")]
    [ProducesResponseType(typeof(AuditLogFilters), StatusCodes.Status200OK)]
    public async Task<IActionResult> FiltersAsync(CancellationToken cancellationToken)
    {
        Result<AuditLogFilters> result = await _sender.Send(new GetAuditLogFiltersQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
