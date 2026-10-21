using ERP.Application.Abstractions.Messaging;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Service;

/// <summary>One part used or the labour charged, as entered.</summary>
/// <param name="ProductId">The part, or the service product the labour is charged as.</param>
/// <param name="Quantity">How many, in the product's stock unit.</param>
/// <param name="Rate">What one is charged at.</param>
/// <param name="Remarks">A remark.</param>
/// <param name="BatchId">The batch the part comes out of, for a product tracked in batches.</param>
public sealed record JobCardLineInput(
    Guid ProductId,
    decimal Quantity,
    decimal Rate,
    string? Remarks = null,
    Guid? BatchId = null);

/// <summary>Everything a job card says, as entered.</summary>
/// <param name="CustomerLedgerId">The customer's account, where they have one.</param>
/// <param name="Customer">Who brought the device in.</param>
/// <param name="ReferenceNumber">A reference they gave.</param>
/// <param name="PaymentMode">Whether they pay at the counter or on account.</param>
/// <param name="Device">The device.</param>
/// <param name="Work">What is wrong and what is to be done.</param>
/// <param name="SalesmanLedgerId">Who took the job in.</param>
/// <param name="TechnicianLedgerId">Who is doing the work.</param>
/// <param name="Lines">The parts and labour, whole.</param>
public sealed record JobCardInput(
    Guid? CustomerLedgerId,
    JobCardCustomer Customer,
    string? ReferenceNumber,
    SalesPaymentMode PaymentMode,
    JobCardDevice Device,
    JobCardWork Work,
    Guid? SalesmanLedgerId,
    Guid? TechnicianLedgerId,
    IReadOnlyList<JobCardLineInput> Lines);

/// <summary>Takes a device in for service.</summary>
/// <param name="Date">The day it was taken in.</param>
/// <param name="WarehouseId">The godown parts are drawn from.</param>
/// <param name="Card">What the card says.</param>
public sealed record CreateJobCardCommand(DateOnly Date, Guid WarehouseId, JobCardInput Card)
    : ICommand<Guid>, ITransactional;

/// <summary>Replaces what a job card says, keeping its number and date.</summary>
/// <param name="JobCardId">The card.</param>
/// <param name="Card">What it says now. Its parts and labour are ignored once the job is billed.</param>
public sealed record UpdateJobCardCommand(Guid JobCardId, JobCardInput Card) : ICommand, ITransactional;

/// <summary>Ticks a stage of a job on a day, or clears it.</summary>
/// <param name="JobCardId">The card.</param>
/// <param name="Stage">The stage.</param>
/// <param name="On">The day it happened, or null to clear it.</param>
public sealed record MarkJobCardStageCommand(Guid JobCardId, JobCardStage Stage, DateOnly? On)
    : ICommand, ITransactional;

/// <summary>Withdraws a job.</summary>
/// <param name="JobCardId">The card.</param>
/// <param name="Reason">Why.</param>
public sealed record CancelJobCardCommand(Guid JobCardId, string Reason) : ICommand, ITransactional;

/// <summary>Raises a draft sales invoice for a job's parts and labour.</summary>
/// <param name="JobCardId">The card.</param>
/// <param name="CustomerLedgerId">
/// The account to bill, where the card names none: a walk-in customer is billed to the
/// firm's cash customer.
/// </param>
/// <param name="Date">The invoice date; today where none is given.</param>
public sealed record BillJobCardCommand(Guid JobCardId, Guid? CustomerLedgerId = null, DateOnly? Date = null)
    : ICommand<SalesInvoiceResponse>, ITransactional;

/// <summary>Lists job cards.</summary>
/// <param name="Status">Only cards standing here.</param>
/// <param name="Search">Text in the number, the customer's name or mobile, the device or its serial.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
public sealed record ListJobCardsQuery(
    JobCardStatus? Status = null,
    string? Search = null,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<IReadOnlyList<JobCardSummary>>;

/// <summary>Reads one job card.</summary>
/// <param name="JobCardId">The card.</param>
public sealed record GetJobCardQuery(Guid JobCardId) : IQuery<JobCardDetail>;

/// <summary>Finds customers by the mobile number they give at the counter.</summary>
/// <param name="Mobile">The number, or the start of it.</param>
public sealed record FindJobCardCustomersQuery(string Mobile) : IQuery<IReadOnlyList<JobCardCustomerMatch>>;

/// <summary>A job card as the list shows it.</summary>
/// <param name="Id">The card.</param>
/// <param name="Number">Its number.</param>
/// <param name="Date">The day the device was taken in.</param>
/// <param name="CustomerName">Who brought it in.</param>
/// <param name="Mobile">Their mobile number.</param>
/// <param name="ItemName">What the device is.</param>
/// <param name="BrandName">Who made it.</param>
/// <param name="ModelNumber">Its model.</param>
/// <param name="SerialNumber">Its serial number or IMEI.</param>
/// <param name="Fault">What the customer says is wrong.</param>
/// <param name="TechnicianName">Who is doing the work.</param>
/// <param name="EstimatedOn">When it should be ready.</param>
/// <param name="EstimatedCharge">What the customer was told it would cost.</param>
/// <param name="Amount">What the parts and labour come to, before tax.</param>
/// <param name="Status">Where the job stands.</param>
/// <param name="SalesInvoiceId">The invoice that billed it.</param>
/// <param name="SalesInvoiceNumber">That invoice's number.</param>
/// <param name="SalesInvoiceStatus">That invoice's status.</param>
public sealed record JobCardSummary(
    Guid Id,
    string Number,
    DateOnly Date,
    string CustomerName,
    string? Mobile,
    string ItemName,
    string? BrandName,
    string? ModelNumber,
    string? SerialNumber,
    string? Fault,
    string? TechnicianName,
    DateOnly? EstimatedOn,
    decimal EstimatedCharge,
    decimal Amount,
    JobCardStatus Status,
    Guid? SalesInvoiceId,
    string? SalesInvoiceNumber,
    SalesInvoiceStatus? SalesInvoiceStatus);

/// <summary>The day each stage of a job was reached.</summary>
/// <param name="Processing">Work started.</param>
/// <param name="Ready">Repaired.</param>
/// <param name="NotReady">Found it could not be repaired.</param>
/// <param name="Informed">The customer was told.</param>
/// <param name="Delivered">Handed back.</param>
/// <param name="Dead">Found beyond repair.</param>
public sealed record JobCardStages(
    DateOnly? Processing,
    DateOnly? Ready,
    DateOnly? NotReady,
    DateOnly? Informed,
    DateOnly? Delivered,
    DateOnly? Dead);

/// <summary>A part or labour line, as the card shows it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">Its code.</param>
/// <param name="ProductName">Its description.</param>
/// <param name="StockUnitCode">Its stock unit.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Rate">What one is charged at.</param>
/// <param name="Amount">What the line comes to.</param>
/// <param name="Remarks">The remark.</param>
/// <param name="IsStocked">Whether it is a part off the shelf rather than a service.</param>
/// <param name="TracksBatches">Whether billing it has to name a batch.</param>
/// <param name="BatchId">The batch named.</param>
/// <param name="BatchNumber">Its number.</param>
public sealed record JobCardLineDetail(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string StockUnitCode,
    decimal Quantity,
    decimal Rate,
    decimal Amount,
    string? Remarks,
    bool IsStocked,
    bool TracksBatches,
    Guid? BatchId,
    string? BatchNumber);

/// <summary>A job card in full.</summary>
/// <param name="Summary">What the list shows.</param>
/// <param name="WarehouseId">The godown parts are drawn from.</param>
/// <param name="WarehouseName">The godown's name.</param>
/// <param name="CustomerLedgerId">The customer's account.</param>
/// <param name="CustomerLedgerName">The account's name.</param>
/// <param name="Customer">Who brought the device in.</param>
/// <param name="ReferenceNumber">A reference they gave.</param>
/// <param name="PaymentMode">Whether they pay at the counter or on account.</param>
/// <param name="Device">The device.</param>
/// <param name="Work">What is wrong and what is to be done.</param>
/// <param name="SalesmanLedgerId">Who took the job in.</param>
/// <param name="TechnicianLedgerId">Who is doing the work.</param>
/// <param name="Stages">The day each stage was reached.</param>
/// <param name="CancellationReason">Why it was cancelled.</param>
/// <param name="Lines">The parts and labour.</param>
public sealed record JobCardDetail(
    JobCardSummary Summary,
    Guid WarehouseId,
    string WarehouseName,
    Guid? CustomerLedgerId,
    string? CustomerLedgerName,
    JobCardCustomer Customer,
    string? ReferenceNumber,
    SalesPaymentMode PaymentMode,
    JobCardDevice Device,
    JobCardWork Work,
    Guid? SalesmanLedgerId,
    Guid? TechnicianLedgerId,
    JobCardStages Stages,
    string? CancellationReason,
    IReadOnlyList<JobCardLineDetail> Lines);

/// <summary>A customer found by their mobile number.</summary>
/// <param name="CustomerLedgerId">Their account, where they have one.</param>
/// <param name="Customer">Their details as last recorded.</param>
public sealed record JobCardCustomerMatch(Guid? CustomerLedgerId, JobCardCustomer Customer);

/// <summary>Reads job cards for the screens.</summary>
public interface IJobCardReader
{
    /// <summary>Lists a firm's job cards, newest first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="query">The filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cards.</returns>
    Task<IReadOnlyList<JobCardSummary>> ListAsync(
        FirmId firmId,
        ListJobCardsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one card.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="id">The card.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The card, or <see langword="null"/>.</returns>
    Task<JobCardDetail?> FindAsync(FirmId firmId, JobCardId id, CancellationToken cancellationToken = default);

    /// <summary>Finds customers by mobile number: from earlier cards, then from customer accounts.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="mobile">The number, or the start of it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Up to ten matches.</returns>
    Task<IReadOnlyList<JobCardCustomerMatch>> FindCustomersAsync(
        FirmId firmId,
        string mobile,
        CancellationToken cancellationToken = default);
}

/// <summary>Validates what a job card says.</summary>
public sealed class JobCardInputValidator : AbstractValidator<JobCardInput>
{
    /// <summary>Initialises a new instance of the <see cref="JobCardInputValidator"/> class.</summary>
    public JobCardInputValidator()
    {
        RuleFor(c => c.Customer).NotNull();
        RuleFor(c => c.Customer.Name).NotEmpty().WithMessage("Say who brought the device in.")
            .When(c => c.Customer is not null);
        RuleFor(c => c.Device).NotNull();
        RuleFor(c => c.Device.ItemName).NotEmpty().WithMessage("Say what the device is.")
            .When(c => c.Device is not null);
        RuleFor(c => c.Work).NotNull();
        RuleFor(c => c.PaymentMode).IsInEnum();
        RuleFor(c => c.Lines).NotNull();
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.Rate).GreaterThanOrEqualTo(0m);
            line.RuleFor(l => l.Remarks).MaximumLength(JobCard.MaximumLongLength);
        });
    }
}

/// <summary>Validates <see cref="CreateJobCardCommand"/>.</summary>
public sealed class CreateJobCardCommandValidator : AbstractValidator<CreateJobCardCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateJobCardCommandValidator"/> class.</summary>
    public CreateJobCardCommandValidator()
    {
        RuleFor(c => c.WarehouseId).NotEqual(Guid.Empty);
        RuleFor(c => c.Card).NotNull().SetValidator(new JobCardInputValidator());
    }
}

/// <summary>Validates <see cref="UpdateJobCardCommand"/>.</summary>
public sealed class UpdateJobCardCommandValidator : AbstractValidator<UpdateJobCardCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateJobCardCommandValidator"/> class.</summary>
    public UpdateJobCardCommandValidator()
    {
        RuleFor(c => c.JobCardId).NotEqual(Guid.Empty);
        RuleFor(c => c.Card).NotNull().SetValidator(new JobCardInputValidator());
    }
}

/// <summary>Validates <see cref="CancelJobCardCommand"/>.</summary>
public sealed class CancelJobCardCommandValidator : AbstractValidator<CancelJobCardCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelJobCardCommandValidator"/> class.</summary>
    public CancelJobCardCommandValidator()
    {
        RuleFor(c => c.JobCardId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(JobCard.MaximumLongLength);
    }
}

/// <summary>Validates <see cref="MarkJobCardStageCommand"/>.</summary>
public sealed class MarkJobCardStageCommandValidator : AbstractValidator<MarkJobCardStageCommand>
{
    /// <summary>Initialises a new instance of the <see cref="MarkJobCardStageCommandValidator"/> class.</summary>
    public MarkJobCardStageCommandValidator()
    {
        RuleFor(c => c.JobCardId).NotEqual(Guid.Empty);
        RuleFor(c => c.Stage).IsInEnum();
    }
}
