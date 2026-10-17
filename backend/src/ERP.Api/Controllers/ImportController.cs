using System.Text;
using Asp.Versioning;
using ERP.Application.Platform.Import;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Bringing a firm's data across from EasyRetail.</summary>
/// <remarks>
/// <para>
/// The data reaches the screen one of two ways - the shop runs the export script and
/// uploads its file, or the server reads the database itself - and the screen then sends
/// it here in four steps: masters, products in batches, balances, and stock in batches.
/// Each is short, so none outlasts a proxy's timeout, and the screen can say how far it
/// has got.
/// </para>
/// <para>
/// Every step can be repeated. What was imported before is recognised by its EasyRetail
/// identifier and left alone, so a run that stopped half way is finished by running it
/// again.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/import/easyretail")]
[Authorize]
[Produces("application/json")]
public sealed class ImportController : ApiControllerBase
{
    private readonly IEasyRetailSource _source;
    private readonly IEasyRetailImporter _importer;

    /// <summary>Initialises a new instance of the <see cref="ImportController"/> class.</summary>
    /// <param name="source">Reads EasyRetail, or writes the script that does.</param>
    /// <param name="importer">Brings the data in.</param>
    public ImportController(IEasyRetailSource source, IEasyRetailImporter importer)
    {
        _source = source;
        _importer = importer;
    }

    /// <summary>Downloads the PowerShell script that exports an EasyRetail database to a file.</summary>
    /// <returns>The script.</returns>
    /// <response code="200">The script, to run on the computer that has EasyRetail.</response>
    [HttpGet("script")]
    [RequiresPermission("platform", "firm", "edit")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public IActionResult Script()
    {
        // With a byte order mark: Windows PowerShell 5.1 reads a file without one in the
        // machine's own code page.
        byte[] script = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_source.ExportScript())];

        return File(script, "text/plain; charset=utf-8", "easyretail-export.ps1");
    }

    /// <summary>Reads an EasyRetail database the server can reach.</summary>
    /// <param name="connection">Where it is and how to sign in.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The same document the export script writes.</returns>
    /// <response code="200">The export.</response>
    /// <response code="422">The database could not be reached or read; the message says why.</response>
    [HttpPost("read")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReadAsync(
        [FromBody] EasyRetailConnection connection,
        CancellationToken cancellationToken)
    {
        Result<string> read = await _source.ReadAsync(connection, cancellationToken);

        return read.IsSuccess ? Content(read.Value, "application/json", Encoding.UTF8) : Problem(read.Error);
    }

    /// <summary>Imports the groups, ledgers, customers, suppliers and inventory masters.</summary>
    /// <param name="request">The masters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was created, matched and skipped.</returns>
    /// <response code="200">Done.</response>
    [HttpPost("masters")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(ImportStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> MastersAsync(
        [FromBody] ImportMastersRequest request,
        CancellationToken cancellationToken)
    {
        Result<ImportStepResult> result = await _importer.ImportMastersAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Imports a batch of products, with their barcodes.</summary>
    /// <param name="request">The products.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was created, matched and skipped.</returns>
    /// <response code="200">Done.</response>
    /// <response code="422">The masters have not been imported yet.</response>
    [HttpPost("products")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(ImportStepResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ProductsAsync(
        [FromBody] ImportProductsRequest request,
        CancellationToken cancellationToken)
    {
        Result<ImportStepResult> result = await _importer.ImportProductsAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Sets every imported ledger's opening balance.</summary>
    /// <param name="request">The balances.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The totals set, and any difference carried.</returns>
    /// <response code="200">Done.</response>
    [HttpPost("balances")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(ImportBalancesResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BalancesAsync(
        [FromBody] ImportBalancesRequest request,
        CancellationToken cancellationToken)
    {
        Result<ImportBalancesResult> result = await _importer.ImportBalancesAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Enters a batch of opening stock.</summary>
    /// <param name="request">The stock.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was created and skipped.</returns>
    /// <response code="200">Done.</response>
    /// <response code="422">The stock could not be posted; the message says why.</response>
    [HttpPost("stock")]
    [RequiresPermission("platform", "firm", "edit")]
    [ProducesResponseType(typeof(ImportStepResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> StockAsync(
        [FromBody] ImportStockRequest request,
        CancellationToken cancellationToken)
    {
        Result<ImportStepResult> result = await _importer.ImportStockAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
