using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Sales;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>The customer master of section 12.1.</summary>
/// <remarks>
/// <para>
/// A customer is a sub-ledger, which is what section 7.1 calls it and what the rest of the
/// system already assumes: an invoice is billed to one, a receipt settles against one, and
/// the debtors report sums them. So these endpoints create and maintain ledgers of kind
/// <c>Customer</c> rather than a parallel record that would have to be kept in step.
/// </para>
/// <para>
/// Nothing here deletes. A customer with history is what every past invoice and the
/// debtors report point at; withdrawing one stops new documents naming them and leaves the
/// trail intact.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sales/customers")]
[Authorize]
[Produces("application/json")]
public sealed class CustomersController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="CustomersController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public CustomersController(ISender sender) => _sender = sender;

    /// <summary>Lists customers, or finds one by what a counter can read off a phone.</summary>
    /// <param name="search">Matched against code, name and mobile number.</param>
    /// <param name="activeOnly">Whether to exclude withdrawn customers.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The customers, by name.</returns>
    /// <response code="200">The customers.</response>
    /// <remarks>
    /// One search across all three fields rather than a parameter each, because section
    /// 12.1's lookup is somebody typing whatever they have - a number, part of a name -
    /// into one box while a customer waits.
    /// </remarks>
    [HttpGet]
    [RequiresPermission("sales", "customer", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? search,
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CustomerResponse>> result = await _sender.Send(
            new GetCustomersQuery(search, activeOnly), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one customer.</summary>
    /// <param name="id">The customer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The customer, with their terms and contact details.</returns>
    /// <response code="200">The customer as the system holds them.</response>
    /// <response code="404">No such customer in the selected firm.</response>
    [HttpGet("{id:guid}")]
    [RequiresPermission("sales", "customer", "view")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<CustomerResponse> result = await _sender.Send(
            new GetCustomerQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Creates a customer.</summary>
    /// <param name="request">Their code, name, terms and contact details.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The customer as created.</returns>
    /// <response code="201">The customer.</response>
    /// <response code="400">The details were refused, and the reason says why.</response>
    /// <response code="409">The code is already used by another account.</response>
    [HttpPost]
    [RequiresPermission("sales", "customer", "create")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        Result<CustomerResponse> result = await _sender.Send(request, cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync)) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Changes a customer's details.</summary>
    /// <param name="id">The customer.</param>
    /// <param name="request">The details to change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The customer as it now stands.</returns>
    /// <response code="200">The changed customer.</response>
    /// <response code="404">No such customer in the selected firm.</response>
    /// <remarks>
    /// The code is not changeable. It is what a firm's own records and any imported
    /// history refer to a customer by, and renaming it silently would leave both pointing
    /// at nothing.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<CustomerResponse> result = await _sender.Send(
            request with { CustomerId = id }, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Withdraws a customer from use, or puts them back.</summary>
    /// <param name="id">The customer.</param>
    /// <param name="request">Whether they may be billed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The customer as it now stands.</returns>
    /// <response code="200">The changed customer.</response>
    /// <response code="404">No such customer in the selected firm.</response>
    [HttpPut("{id:guid}/active")]
    [RequiresPermission("sales", "customer", "edit")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(
        Guid id,
        [FromBody] SetCustomerActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<CustomerResponse> result = await _sender.Send(
            new SetCustomerActiveCommand(id, request.IsActive), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}

/// <summary>Whether a customer may be billed.</summary>
/// <param name="IsActive">True to restore them, false to withdraw them.</param>
/// <remarks>
/// Required rather than defaulted, because the default of a boolean is <c>false</c> and a
/// caller who left it out would withdraw the customer they meant to restore.
/// </remarks>
public sealed record SetCustomerActiveRequest([property: JsonRequired] bool IsActive);
