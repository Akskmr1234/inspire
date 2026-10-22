using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Reports;
using ERP.Application.Parties;
using ERP.Domain.Accounting;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Ledgers - the posting accounts of the chart of accounts.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounting/ledgers")]
[Authorize]
[Produces("application/json")]
public sealed class LedgersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="LedgersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public LedgersController(ISender sender) => _sender = sender;

    /// <summary>Lists the selected firm's ledgers.</summary>
    /// <param name="activeOnly">Whether to exclude deactivated ledgers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ledgers, ordered by account group then ledger code.</returns>
    /// <remarks>
    /// Unpaged by design: a chart of accounts is a few hundred rows, and callers
    /// filter client-side as the user types.
    /// </remarks>
    /// <response code="200">The ledgers.</response>
    /// <response code="403">No firm is selected, or permission is lacking.</response>
    [HttpGet]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<LedgerSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<LedgerSummary>> result =
            await _sender.Send(new GetLedgersQuery(activeOnly), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads where a party stands against their credit limit.</summary>
    /// <param name="id">The party's ledger.</param>
    /// <param name="asAt">The date to state the position as at. Defaults to today.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What they owe, what they were allowed to owe, and what is left.</returns>
    /// <remarks>
    /// Read from the open bills rather than from the ledger balance. The two answer
    /// different questions: a balance includes payments on account and everything else
    /// posted to the party, while a credit limit is about invoices they have not paid.
    /// A customer who has paid in advance is not using credit, and a balance would say
    /// they were.
    /// <para>
    /// Nothing here refuses anything. A limit warns rather than blocks, so this reports
    /// the position and the document that asks decides what to do about it.
    /// </para>
    /// </remarks>
    /// <response code="200">The credit position.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpGet("{id:guid}/credit-status")]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(CreditStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCreditStatusAsync(
        Guid id,
        [FromQuery] DateOnly? asAt,
        CancellationToken cancellationToken)
    {
        Result<CreditStatus> result = await _sender.Send(
            new GetCreditStatusQuery(id, asAt), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one ledger with everything its form shows.</summary>
    /// <param name="id">The ledger to read.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Its code, name, group, kind, opening balance and contact details.</returns>
    /// <response code="200">The ledger.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(LedgerDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<LedgerDetail> result = await _sender.Send(new GetLedgerQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Creates a ledger: an expense or income head, a bank account, cash, an employee.</summary>
    /// <param name="request">The ledger.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ledger as created.</returns>
    /// <response code="201">Created.</response>
    /// <response code="400">A detail is missing, or the kind belongs on another screen.</response>
    /// <response code="409">The code is already used.</response>
    [HttpPost]
    [RequiresPermission("accounting", "ledger", "create")]
    [ProducesResponseType(typeof(LedgerDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveLedgerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<LedgerDetail> result = await _sender.Send(
            new CreateLedgerCommand(request.Code, request.ToInput()), cancellationToken);

        return result.IsSuccess
            ? Created($"{Request.Path.Value?.TrimEnd('/')}/{result.Value.LedgerId}", result.Value)
            : Problem(result.Error);
    }

    /// <summary>Corrects a ledger. Its code stays as it is.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="request">The ledger as it should be.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ledger as it now stands.</returns>
    /// <response code="200">Saved.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    /// <response code="422">A customer, supplier, tax or charge ledger, changed on its own screen.</response>
    [HttpPut("{id:guid}")]
    [RequiresPermission("accounting", "ledger", "edit")]
    [ProducesResponseType(typeof(LedgerDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveLedgerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<LedgerDetail> result = await _sender.Send(
            new UpdateLedgerCommand(id, request.ToInput()), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads the photographs of an employee, customer or supplier.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Up to three pictures as data URLs, in slot order; null where a slot is empty.</returns>
    /// <response code="200">The photographs.</response>
    /// <response code="404">No such party in the selected firm.</response>
    [HttpGet("{id:guid}/photos")]
    [RequiresPermission("accounting", "ledger", "view")]
    [ProducesResponseType(typeof(PhotoSetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotosAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<PhotoSetResponse> result = await _sender.Send(
            new ListPartyPhotosQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Sets or removes one of the photographs of an employee, customer or supplier.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="slot">Which photograph, one to three.</param>
    /// <param name="request">The picture as a data URL; null removes it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Nothing, on success.</returns>
    /// <response code="204">Saved.</response>
    /// <response code="400">Not a picture, too large, or no such slot.</response>
    /// <response code="404">No such party in the selected firm.</response>
    [HttpPut("{id:guid}/photos/{slot:int}")]
    [RequiresPermission("accounting", "ledger", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPhotoAsync(
        Guid id,
        int slot,
        [FromBody] PartyPhotoResponse request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new SetPartyPhotoCommand(id, request.Content, slot), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a ledger from use, or puts it back.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="request">Whether it may be posted to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ledger as it now stands.</returns>
    /// <response code="200">Saved.</response>
    /// <response code="404">No such ledger in the selected firm.</response>
    [HttpPost("{id:guid}/active")]
    [RequiresPermission("accounting", "ledger", "edit")]
    [ProducesResponseType(typeof(LedgerDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] SetLedgerActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<LedgerDetail> result = await _sender.Send(
            new SetLedgerActiveCommand(id, request.IsActive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>A ledger as the chart of accounts enters or corrects it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="AccountGroupId">The group it reports under.</param>
/// <param name="Kind">General, cash, bank or employee.</param>
/// <param name="Code">Its code, on creation only. Made from the name when omitted.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="OpeningBalance">What stood on it when the books were taken on.</param>
/// <param name="OpeningSide">Which side that balance falls on.</param>
/// <param name="Phone">A telephone number.</param>
/// <param name="Email">An email address.</param>
/// <param name="Address">An address, or a bank's branch and account number.</param>
public sealed record SaveLedgerRequest(
    string Name,
    [property: JsonRequired] Guid AccountGroupId,
    [property: JsonRequired] LedgerKind Kind,
    string? Code = null,
    string? NameArabic = null,
    decimal OpeningBalance = 0m,
    EntrySide OpeningSide = EntrySide.Debit,
    string? Phone = null,
    string? Email = null,
    string? Address = null)
{
    /// <summary>The ledger as the command takes it.</summary>
    /// <returns>The input.</returns>
    public LedgerInput ToInput() =>
        new(Name, NameArabic, AccountGroupId, Kind, OpeningBalance, OpeningSide, Phone, Email, Address);
}

/// <summary>Whether a ledger may be posted to.</summary>
/// <param name="IsActive">True to allow postings, false to withdraw it.</param>
public sealed record SetLedgerActiveRequest([property: JsonRequired] bool IsActive);
