using Asp.Versioning;
using ERP.Application.Platform.Menus;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The navigation menu, resolved for the signed-in user.</summary>
/// <remarks>
/// The menu is stored per firm rather than compiled into the client, so an
/// administrator can show, hide, reorder, and regroup entries without a release. The
/// client asks for it once a session begins and renders whatever comes back.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/menu")]
[Authorize]
[Produces("application/json")]
public sealed class MenuController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="MenuController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public MenuController(ISender sender) => _sender = sender;

    /// <summary>Returns the menu the signed-in user may see in the selected firm.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The top-level entries, each with its children, in display order.</returns>
    /// <remarks>
    /// Entries whose permission the caller does not hold are left out, as are headings
    /// left empty once their children have been. This is a courtesy rather than a
    /// security boundary - every endpoint authorises for itself - but a menu offering
    /// screens that refuse the person who clicks them is worse than a short one.
    /// </remarks>
    /// <response code="200">The resolved menu.</response>
    /// <response code="403">No firm is selected, or no user is signed in.</response>
    [HttpGet]
    [ProducesResponseType(typeof(MenuResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMenuAsync(CancellationToken cancellationToken)
    {
        Result<MenuResponse> result = await _sender.Send(new GetMenuQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
