using System.Text.Json.Serialization;
using Asp.Versioning;
using ERP.Application.Sales;
using ERP.Application.Service;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Identity.Authorization;
using ERP.SharedKernel.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

/// <summary>Service job cards: devices taken in for repair, worked, billed and handed back.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/service/job-cards")]
[Authorize]
[Produces("application/json")]
public sealed class JobCardsController : ApiControllerBase
{
    private readonly ISender _sender;

    /// <summary>Initialises a new instance of the <see cref="JobCardsController"/> class.</summary>
    /// <param name="sender">The request dispatcher.</param>
    public JobCardsController(ISender sender) => _sender = sender;

    /// <summary>Lists job cards, newest first.</summary>
    /// <param name="status">Only cards standing here.</param>
    /// <param name="search">Text in the number, customer, mobile, device or serial.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The cards.</returns>
    [HttpGet]
    [RequiresPermission("service", "job-card", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<JobCardSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] JobCardStatus? status,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<JobCardSummary>> result = await _sender.Send(
            new ListJobCardsQuery(status, search, from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Finds customers by the mobile number they give at the counter.</summary>
    /// <param name="mobile">The number, or part of it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Up to ten customers: from earlier jobs first, then from customer accounts.</returns>
    [HttpGet("customers")]
    [RequiresPermission("service", "job-card", "view")]
    [ProducesResponseType(typeof(IReadOnlyList<JobCardCustomerMatch>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindCustomersAsync(
        [FromQuery] string? mobile,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<JobCardCustomerMatch>> result = await _sender.Send(
            new FindJobCardCustomersQuery(mobile ?? string.Empty), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one job card with its parts and labour.</summary>
    /// <param name="id">The card's identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The card.</returns>
    [HttpGet("{id:guid}")]
    [RequiresPermission("service", "job-card", "view")]
    [ProducesResponseType(typeof(JobCardDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Result<JobCardDetail> result = await _sender.Send(new GetJobCardQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Takes a device in.</summary>
    /// <param name="request">The card.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new card's identifier.</returns>
    [HttpPost]
    [RequiresPermission("service", "job-card", "create")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] JobCardRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Date is not { } date || request.WarehouseId is not { } warehouse)
        {
            return Problem(Error.Validation(
                "JobCard.DateAndGodownRequired", "Give the date the device was taken in and the godown."));
        }

        Result<Guid> result = await _sender.Send(
            new CreateJobCardCommand(date, warehouse, request.ToInput()), cancellationToken);

        return result.IsSuccess
            ? Created(Url.Action(nameof(GetAsync), new { id = result.Value }) ?? string.Empty, result.Value)
            : Problem(result.Error);
    }

    /// <summary>Replaces what a card says. Its number, date and godown do not change.</summary>
    /// <param name="id">The card's identifier.</param>
    /// <param name="request">The card, whole.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPut("{id:guid}")]
    [RequiresPermission("service", "job-card", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] JobCardRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(new UpdateJobCardCommand(id, request.ToInput()), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Ticks a stage of the job on a day, or clears it.</summary>
    /// <param name="id">The card's identifier.</param>
    /// <param name="request">The stage and the day; no day clears it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/stage")]
    [RequiresPermission("service", "job-card", "edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkStageAsync(
        Guid id,
        [FromBody] JobCardStageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(
            new MarkJobCardStageCommand(id, request.Stage, request.On), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Withdraws a job.</summary>
    /// <param name="id">The card's identifier.</param>
    /// <param name="request">Why.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission("service", "job-card", "delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelAsync(
        Guid id,
        [FromBody] JobCardCancelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await _sender.Send(new CancelJobCardCommand(id, request.Reason), cancellationToken);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Raises a draft sales invoice for the job's parts and labour.</summary>
    /// <param name="id">The card's identifier.</param>
    /// <param name="request">The account to bill where the card names none, and the date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The draft invoice.</returns>
    [HttpPost("{id:guid}/sales-invoice")]
    [RequiresPermission("sales", "invoice", "create")]
    [ProducesResponseType(typeof(SalesInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BillAsync(
        Guid id,
        [FromBody] JobCardBillRequest? request,
        CancellationToken cancellationToken)
    {
        Result<SalesInvoiceResponse> result = await _sender.Send(
            new BillJobCardCommand(id, request?.CustomerLedgerId, request?.Date), cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/sales/invoices/{result.Value.SalesInvoiceId}", result.Value)
            : Problem(result.Error);
    }
}

/// <summary>A job card, as entered.</summary>
/// <param name="Date">The day the device was taken in. Read only when the card is created.</param>
/// <param name="WarehouseId">The godown parts are drawn from. Read only when the card is created.</param>
/// <param name="CustomerLedgerId">The customer's account, where they have one.</param>
/// <param name="Customer">Who brought the device in.</param>
/// <param name="ReferenceNumber">A reference they gave.</param>
/// <param name="PaymentMode">Whether they pay at the counter or on account.</param>
/// <param name="Device">The device.</param>
/// <param name="Work">What is wrong and what is to be done.</param>
/// <param name="SalesmanLedgerId">Who took the job in.</param>
/// <param name="TechnicianLedgerId">Who is doing the work.</param>
/// <param name="Lines">The parts and labour.</param>
public sealed record JobCardRequest(
    DateOnly? Date,
    Guid? WarehouseId,
    Guid? CustomerLedgerId,
    [property: JsonRequired] JobCardCustomer Customer,
    string? ReferenceNumber,
    [property: JsonRequired] SalesPaymentMode PaymentMode,
    [property: JsonRequired] JobCardDevice Device,
    JobCardWork? Work,
    Guid? SalesmanLedgerId,
    Guid? TechnicianLedgerId,
    IReadOnlyList<JobCardLineInput>? Lines)
{
    /// <summary>Converts the request to what the commands take.</summary>
    /// <returns>The card's content.</returns>
    public JobCardInput ToInput() => new(
        CustomerLedgerId,
        Customer,
        ReferenceNumber,
        PaymentMode,
        Device,
        Work ?? new JobCardWork(),
        SalesmanLedgerId,
        TechnicianLedgerId,
        Lines ?? []);
}

/// <summary>A stage and the day it was reached.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="On">The day; none clears the stage.</param>
public sealed record JobCardStageRequest([property: JsonRequired] JobCardStage Stage, DateOnly? On);

/// <summary>Why a job is withdrawn.</summary>
/// <param name="Reason">The reason.</param>
public sealed record JobCardCancelRequest([property: JsonRequired] string Reason);

/// <summary>How a job is billed.</summary>
/// <param name="CustomerLedgerId">The account to bill where the card names none.</param>
/// <param name="Date">The invoice date, if not today.</param>
public sealed record JobCardBillRequest(Guid? CustomerLedgerId = null, DateOnly? Date = null);
