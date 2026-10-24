using Asp.Versioning;
using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Sales and purchases added up by month, day, bill, item and more, with their profit.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports/transactions")]
[Authorize]
[Produces("application/json")]
public sealed class TransactionReportsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="TransactionReportsController"/> class.</summary>
    /// <param name="sender">The mediator.</param>
    public TransactionReportsController(ISender sender) => _sender = sender;

    /// <summary>Reads a transaction report.</summary>
    /// <param name="document">Sales or purchases.</param>
    /// <param name="from">The first date.</param>
    /// <param name="to">The last date.</param>
    /// <param name="groupBy">What the rows are: month, day, bill, item, brand, category, salesman or party.</param>
    /// <param name="partyLedgerId">One customer or supplier.</param>
    /// <param name="salesmanLedgerId">One salesman.</param>
    /// <param name="productId">One product.</param>
    /// <param name="mode">One tax mode.</param>
    /// <param name="warehouseId">One godown.</param>
    /// <param name="brandId">One brand.</param>
    /// <param name="categoryId">One category.</param>
    /// <param name="areaId">One area.</param>
    /// <param name="routeId">One route.</param>
    /// <param name="taxPercentage">One tax rate.</param>
    /// <param name="serialNumber">One serial number.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rows and their totals.</returns>
    /// <response code="200">The report.</response>
    /// <response code="400">The period or a filter is invalid.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet]
    [RequiresPermission("accounting", "report", "view")]
    [ProducesResponseType(typeof(TransactionReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] TransactionReportDocument document,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] TransactionReportGrouping groupBy = TransactionReportGrouping.Month,
        [FromQuery] Guid? partyLedgerId = null,
        [FromQuery] Guid? salesmanLedgerId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] TaxMode? mode = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] Guid? brandId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? areaId = null,
        [FromQuery] Guid? routeId = null,
        [FromQuery] decimal? taxPercentage = null,
        [FromQuery] string? serialNumber = null,
        CancellationToken cancellationToken = default)
    {
        Result<TransactionReport> result = await _sender.Send(
            new GetTransactionReportQuery(
                document,
                from,
                to,
                groupBy,
                partyLedgerId,
                salesmanLedgerId,
                productId,
                mode,
                warehouseId,
                brandId,
                categoryId,
                areaId,
                routeId,
                taxPercentage,
                serialNumber),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
