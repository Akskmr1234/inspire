using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Service;

/// <summary>Identifies a service job card.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct JobCardId(Guid Value) : IStronglyTypedId<JobCardId>
{
    /// <inheritdoc />
    public static JobCardId From(Guid value) => new(value);

    /// <inheritdoc />
    public static JobCardId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one part or labour line of a job card.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct JobCardLineId(Guid Value) : IStronglyTypedId<JobCardLineId>
{
    /// <inheritdoc />
    public static JobCardLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static JobCardLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Where a job stands, read from the stages ticked on it.</summary>
public enum JobCardStatus
{
    /// <summary>Taken in; nobody has started on it.</summary>
    Received = 1,

    /// <summary>Being worked on.</summary>
    Processing = 2,

    /// <summary>Repaired and ready to go back.</summary>
    Ready = 3,

    /// <summary>Could not be repaired, and is going back as it came.</summary>
    NotReady = 4,

    /// <summary>The customer has been told it can be collected.</summary>
    Informed = 5,

    /// <summary>Handed back to the customer.</summary>
    Delivered = 6,

    /// <summary>Beyond repair.</summary>
    Dead = 7,

    /// <summary>Withdrawn: taken in by mistake, or the customer took it away unrepaired.</summary>
    Cancelled = 8,
}

/// <summary>The stages a job card ticks off, each with the day it happened.</summary>
public enum JobCardStage
{
    /// <summary>Work started.</summary>
    Processing = 1,

    /// <summary>Repaired.</summary>
    Ready = 2,

    /// <summary>Could not be repaired.</summary>
    NotReady = 3,

    /// <summary>The customer was told.</summary>
    Informed = 4,

    /// <summary>Handed back.</summary>
    Delivered = 5,

    /// <summary>Beyond repair.</summary>
    Dead = 6,
}

/// <summary>The customer's details as the job card records them.</summary>
/// <param name="Name">Who brought it in.</param>
/// <param name="Mobile">Their mobile number, which is how they are found again.</param>
/// <param name="AlternateMobile">A second number.</param>
/// <param name="Email">Their e-mail address.</param>
/// <param name="AddressLine1">The first line of their address.</param>
/// <param name="AddressLine2">The second line.</param>
public sealed record JobCardCustomer(
    string Name,
    string? Mobile = null,
    string? AlternateMobile = null,
    string? Email = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null);

/// <summary>The device brought in.</summary>
/// <param name="ItemName">What it is.</param>
/// <param name="BrandName">Who made it.</param>
/// <param name="ModelNumber">Its model.</param>
/// <param name="Colour">Its colour.</param>
/// <param name="SerialNumber">Its serial number or IMEI.</param>
/// <param name="Accessories">What came in with it: charger, case, SIM.</param>
public sealed record JobCardDevice(
    string ItemName,
    string? BrandName = null,
    string? ModelNumber = null,
    string? Colour = null,
    string? SerialNumber = null,
    string? Accessories = null);

/// <summary>What is wrong and what is to be done.</summary>
/// <param name="ServiceType">The kind of job: repair, warranty, installation.</param>
/// <param name="Fault">What the customer says is wrong.</param>
/// <param name="Problem">What the technician found.</param>
/// <param name="Remarks">Remarks for the customer.</param>
/// <param name="Notes">Notes for the workshop.</param>
/// <param name="EstimatedOn">When it should be ready.</param>
/// <param name="EstimatedTime">At what time, as the counter writes it.</param>
/// <param name="EstimatedCharge">What the customer was told it would cost.</param>
public sealed record JobCardWork(
    string? ServiceType = null,
    string? Fault = null,
    string? Problem = null,
    string? Remarks = null,
    string? Notes = null,
    DateOnly? EstimatedOn = null,
    string? EstimatedTime = null,
    decimal EstimatedCharge = 0m);

/// <summary>
/// A device taken in for service: who brought it, what it is, what is wrong, how far the
/// work has got, and the parts and labour that went into it.
/// </summary>
/// <remarks>
/// <para>
/// The card moves no stock and raises no debt. When the job is done its parts and labour
/// become an ordinary sales invoice, which issues the parts and bills the customer the way
/// every other sale does; the card records which invoice that was.
/// </para>
/// <para>
/// The stages are dates rather than one status, because that is how a workshop keeps them:
/// a tick and a day against each. The status is read from the furthest stage reached.
/// </para>
/// </remarks>
public sealed class JobCard : AggregateRoot<JobCardId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a short field - a name, a number, a model - may be.</summary>
    public const int MaximumShortLength = 120;

    /// <summary>The longest a description - a fault, a note - may be.</summary>
    public const int MaximumLongLength = 1000;

    private readonly List<JobCardLine> _lines = [];

    private JobCard(
        JobCardId id,
        TenantId tenantId,
        FirmId firmId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        WarehouseId warehouseId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        FinancialYearId = financialYearId;
        Number = number;
        Date = date;
        WarehouseId = warehouseId;
        Status = JobCardStatus.Received;
        CustomerName = string.Empty;
        ItemName = string.Empty;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private JobCard()
    {
        Number = string.Empty;
        CustomerName = string.Empty;
        ItemName = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the year the card was numbered in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the card number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date the device was taken in.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the godown parts are drawn from.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the customer's account, where they have one.</summary>
    public LedgerId? CustomerLedgerId { get; private set; }

    /// <summary>Gets who brought it in.</summary>
    public string CustomerName { get; private set; }

    /// <summary>Gets their mobile number.</summary>
    public string? Mobile { get; private set; }

    /// <summary>Gets a second number.</summary>
    public string? AlternateMobile { get; private set; }

    /// <summary>Gets their e-mail address.</summary>
    public string? Email { get; private set; }

    /// <summary>Gets the first line of their address.</summary>
    public string? AddressLine1 { get; private set; }

    /// <summary>Gets the second line of their address.</summary>
    public string? AddressLine2 { get; private set; }

    /// <summary>Gets a reference the customer gave.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets whether the job is paid for at the counter or put on account.</summary>
    public SalesPaymentMode PaymentMode { get; private set; } = SalesPaymentMode.Cash;

    /// <summary>Gets who took the job in.</summary>
    public LedgerId? SalesmanLedgerId { get; private set; }

    /// <summary>Gets who is doing the work.</summary>
    public LedgerId? TechnicianLedgerId { get; private set; }

    /// <summary>Gets what the device is.</summary>
    public string ItemName { get; private set; }

    /// <summary>Gets who made it.</summary>
    public string? BrandName { get; private set; }

    /// <summary>Gets its model.</summary>
    public string? ModelNumber { get; private set; }

    /// <summary>Gets its colour.</summary>
    public string? Colour { get; private set; }

    /// <summary>Gets its serial number or IMEI.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Gets what came in with it.</summary>
    public string? Accessories { get; private set; }

    /// <summary>Gets the kind of job.</summary>
    public string? ServiceType { get; private set; }

    /// <summary>Gets what the customer says is wrong.</summary>
    public string? Fault { get; private set; }

    /// <summary>Gets what the technician found.</summary>
    public string? Problem { get; private set; }

    /// <summary>Gets remarks for the customer.</summary>
    public string? Remarks { get; private set; }

    /// <summary>Gets notes for the workshop.</summary>
    public string? Notes { get; private set; }

    /// <summary>Gets when it should be ready.</summary>
    public DateOnly? EstimatedOn { get; private set; }

    /// <summary>Gets at what time.</summary>
    public string? EstimatedTime { get; private set; }

    /// <summary>Gets what the customer was told it would cost.</summary>
    public decimal EstimatedCharge { get; private set; }

    /// <summary>Gets the day work started.</summary>
    public DateOnly? ProcessingOn { get; private set; }

    /// <summary>Gets the day it was repaired.</summary>
    public DateOnly? ReadyOn { get; private set; }

    /// <summary>Gets the day it was found it could not be repaired.</summary>
    public DateOnly? NotReadyOn { get; private set; }

    /// <summary>Gets the day the customer was told.</summary>
    public DateOnly? InformedOn { get; private set; }

    /// <summary>Gets the day it was handed back.</summary>
    public DateOnly? DeliveredOn { get; private set; }

    /// <summary>Gets the day it was found beyond repair.</summary>
    public DateOnly? DeadOn { get; private set; }

    /// <summary>Gets where the job stands.</summary>
    public JobCardStatus Status { get; private set; }

    /// <summary>Gets why it was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Gets the invoice that billed the job.</summary>
    public SalesInvoiceId? SalesInvoiceId { get; private set; }

    /// <summary>Gets the parts and labour.</summary>
    public IReadOnlyList<JobCardLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether the parts and labour may still be changed.</summary>
    public bool IsBillable => SalesInvoiceId is null && Status != JobCardStatus.Cancelled;

    /// <summary>Takes a device in.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="year">The year it is numbered in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The day it was taken in.</param>
    /// <param name="warehouse">The godown parts are drawn from.</param>
    /// <returns>The card, or the reason it was refused.</returns>
    public static Result<JobCard> Create(
        TenantId tenantId,
        FirmId firmId,
        FinancialYear year,
        string number,
        DateOnly date,
        Warehouse warehouse)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<JobCard>(Error.Validation(
                "JobCard.NumberRequired", "A job card number is required."));
        }

        return warehouse.FirmId != firmId
            ? Result.Failure<JobCard>(Error.Validation(
                "JobCard.WarehouseNotInFirm", "The godown belongs to another firm."))
            : Result.Success(new JobCard(
                JobCardId.NewId(), tenantId, firmId, year.Id, number.Trim(), date, warehouse.Id));
    }

    /// <summary>Records who brought the device in.</summary>
    /// <param name="customer">Their details.</param>
    /// <param name="account">Their account, where they have one.</param>
    /// <param name="reference">A reference they gave.</param>
    /// <param name="mode">Whether they pay at the counter or on account.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetCustomer(JobCardCustomer customer, Ledger? account, string? reference, SalesPaymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(customer);

        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            return Result.Failure(Error.Validation(
                "JobCard.CustomerRequired", "Say who brought the device in."));
        }

        if (account is not null && (account.FirmId != FirmId || account.Kind != LedgerKind.Customer))
        {
            return Result.Failure(Error.Validation(
                "JobCard.NotACustomer", $"'{account.Name}' is not a customer of this firm."));
        }

        if (!Enum.IsDefined(mode))
        {
            return Result.Failure(Error.Validation("JobCard.UnknownMode", "Choose cash or credit."));
        }

        CustomerLedgerId = account?.Id;
        CustomerName = Short(customer.Name)!;
        Mobile = Short(customer.Mobile);
        AlternateMobile = Short(customer.AlternateMobile);
        Email = Short(customer.Email);
        AddressLine1 = Long(customer.AddressLine1);
        AddressLine2 = Long(customer.AddressLine2);
        ReferenceNumber = Short(reference);
        PaymentMode = mode;

        return Result.Success();
    }

    /// <summary>Records the device.</summary>
    /// <param name="device">What it is.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDevice(JobCardDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (string.IsNullOrWhiteSpace(device.ItemName))
        {
            return Result.Failure(Error.Validation(
                "JobCard.ItemRequired", "Say what the device is."));
        }

        ItemName = Short(device.ItemName)!;
        BrandName = Short(device.BrandName);
        ModelNumber = Short(device.ModelNumber);
        Colour = Short(device.Colour);
        SerialNumber = Short(device.SerialNumber);
        Accessories = Long(device.Accessories);

        return Result.Success();
    }

    /// <summary>Records what is wrong, what is to be done, and by whom.</summary>
    /// <param name="work">The fault, the diagnosis and the estimate.</param>
    /// <param name="salesman">Who took the job in.</param>
    /// <param name="technician">Who is doing the work.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetWork(JobCardWork work, Ledger? salesman, Ledger? technician)
    {
        ArgumentNullException.ThrowIfNull(work);

        foreach (Ledger? staff in new[] { salesman, technician })
        {
            if (staff is not null && (staff.FirmId != FirmId || staff.Kind != LedgerKind.Employee))
            {
                return Result.Failure(Error.Validation(
                    "JobCard.NotAnEmployee", $"'{staff.Name}' is not an employee of this firm."));
            }
        }

        if (work.EstimatedCharge < 0m)
        {
            return Result.Failure(Error.Validation(
                "JobCard.EstimateNegative", "An estimated charge cannot be negative."));
        }

        SalesmanLedgerId = salesman?.Id;
        TechnicianLedgerId = technician?.Id;
        ServiceType = Short(work.ServiceType);
        Fault = Long(work.Fault);
        Problem = Long(work.Problem);
        Remarks = Long(work.Remarks);
        Notes = Long(work.Notes);
        EstimatedOn = work.EstimatedOn;
        EstimatedTime = Short(work.EstimatedTime);
        EstimatedCharge = work.EstimatedCharge;

        return Result.Success();
    }

    /// <summary>Removes every part and labour line, ahead of them being entered again.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result ClearLines()
    {
        if (!IsBillable)
        {
            return Result.Failure(Locked());
        }

        _lines.Clear();

        return Result.Success();
    }

    /// <summary>Adds a part used or the labour charged.</summary>
    /// <param name="product">The part, or the service product the labour is charged as.</param>
    /// <param name="quantity">How many, in the product's stock unit.</param>
    /// <param name="rate">What one is charged at.</param>
    /// <param name="remarks">A remark.</param>
    /// <param name="batch">The batch the part comes out of, for a product tracked in batches.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    public Result<JobCardLine> AddLine(
        Product product,
        decimal quantity,
        decimal rate,
        string? remarks = null,
        Batch? batch = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!IsBillable)
        {
            return Result.Failure<JobCardLine>(Locked());
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<JobCardLine>(Error.Validation(
                "JobCard.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (quantity <= 0m || rate < 0m)
        {
            return Result.Failure<JobCardLine>(Error.Validation(
                "JobCard.LineInvalid", $"'{product.Code}' needs a quantity above zero and a rate of zero or more."));
        }

        // The batch can wait until the job is billed, which is when the part leaves; but one
        // that is named has to be this product's.
        if (batch is not null && (!product.TracksBatches || batch.ProductId != product.Id))
        {
            return Result.Failure<JobCardLine>(Error.Validation(
                "JobCard.BatchWrong", $"That batch does not belong to '{product.Code}'."));
        }

        JobCardLine line = new(
            JobCardLineId.NewId(), TenantId, Id, product.Id, quantity, rate, _lines.Count + 1, Long(remarks), batch?.Id);

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Ticks a stage on the given day, or clears it.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="on">The day it happened, or null to clear it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result MarkStage(JobCardStage stage, DateOnly? on)
    {
        if (Status == JobCardStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule(
                "JobCard.Cancelled", $"Job card '{Number}' was cancelled."));
        }

        switch (stage)
        {
            case JobCardStage.Processing: ProcessingOn = on; break;
            case JobCardStage.Ready: ReadyOn = on; break;
            case JobCardStage.NotReady: NotReadyOn = on; break;
            case JobCardStage.Informed: InformedOn = on; break;
            case JobCardStage.Delivered: DeliveredOn = on; break;
            case JobCardStage.Dead: DeadOn = on; break;
            default:
                return Result.Failure(Error.Validation("JobCard.UnknownStage", "No such stage."));
        }

        Status = StatusFromStages();

        return Result.Success();
    }

    /// <summary>Records the invoice that billed the job.</summary>
    /// <param name="invoice">The invoice.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RecordInvoice(SalesInvoiceId invoice)
    {
        if (!IsBillable)
        {
            return Result.Failure(Locked());
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "JobCard.NothingToBill", $"Job card '{Number}' has no parts or labour to bill."));
        }

        SalesInvoiceId = invoice;

        return Result.Success();
    }

    /// <summary>Lets the job be billed again, its invoice having been cancelled.</summary>
    public void ReleaseInvoice() => SalesInvoiceId = null;

    /// <summary>Withdraws the job.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Cancel(string reason)
    {
        if (SalesInvoiceId is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "JobCard.Billed", $"Job card '{Number}' has been billed. Cancel its invoice first."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "JobCard.ReasonRequired", "Give a reason for cancelling the job."));
        }

        Status = JobCardStatus.Cancelled;
        CancellationReason = Long(reason);

        return Result.Success();
    }

    private static string? Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Clip(value.Trim(), MaximumShortLength);

    private static string? Long(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Clip(value.Trim(), MaximumLongLength);

    private static string Clip(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private JobCardStatus StatusFromStages()
    {
        if (DeliveredOn is not null)
        {
            return JobCardStatus.Delivered;
        }

        if (DeadOn is not null)
        {
            return JobCardStatus.Dead;
        }

        if (InformedOn is not null)
        {
            return JobCardStatus.Informed;
        }

        if (ReadyOn is not null)
        {
            return JobCardStatus.Ready;
        }

        if (NotReadyOn is not null)
        {
            return JobCardStatus.NotReady;
        }

        return ProcessingOn is not null ? JobCardStatus.Processing : JobCardStatus.Received;
    }

    private Error Locked() => Error.BusinessRule(
        "JobCard.Locked",
        $"Job card '{Number}' has been billed or cancelled; its parts and labour can no longer change.");
}

/// <summary>A part used on a job, or the labour charged for it.</summary>
public sealed class JobCardLine : Entity<JobCardLineId>, ITenantScoped
{
    internal JobCardLine(
        JobCardLineId id,
        TenantId tenantId,
        JobCardId jobCardId,
        ProductId productId,
        decimal quantity,
        decimal rate,
        int lineNumber,
        string? remarks,
        BatchId? batchId)
        : base(id)
    {
        TenantId = tenantId;
        JobCardId = jobCardId;
        ProductId = productId;
        Quantity = quantity;
        Rate = rate;
        LineNumber = lineNumber;
        Remarks = remarks;
        BatchId = batchId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private JobCardLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the card.</summary>
    public JobCardId JobCardId { get; private set; }

    /// <summary>Gets the part, or the service product the labour is charged as.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets how many, in the product's stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets what one is charged at.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets the position on the card, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the remark.</summary>
    public string? Remarks { get; private set; }

    /// <summary>Gets the batch the part comes out of, where one has been named.</summary>
    public BatchId? BatchId { get; private set; }
}
