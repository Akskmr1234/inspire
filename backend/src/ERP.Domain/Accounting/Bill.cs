using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Accounting;

/// <summary>Which direction a bill runs.</summary>
public enum BillType
{
    /// <summary>Money owed to the firm by a customer.</summary>
    Receivable = 1,

    /// <summary>Money the firm owes a supplier.</summary>
    Payable = 2,
}

/// <summary>How much of a bill remains outstanding.</summary>
public enum BillStatus
{
    /// <summary>Nothing has been allocated against it.</summary>
    Open = 1,

    /// <summary>Some but not all of it has been settled.</summary>
    PartiallySettled = 2,

    /// <summary>Fully settled. Nothing further may be allocated.</summary>
    Settled = 3,
}

/// <summary>
/// One outstanding receivable or payable, settled against individually.
/// </summary>
/// <remarks>
/// <para>
/// The specification's bill-wise settlement: a receipt or payment against a
/// bill-wise ledger allocates to specific documents rather than simply moving the
/// party's balance. That distinction is what makes "which invoices are still
/// unpaid" answerable at all - a running balance can tell you a customer owes
/// 12,000, but not whether that is one overdue invoice from March or six current
/// ones, and the aging report exists precisely to tell those apart.
/// </para>
/// <para>
/// A bill is its own aggregate rather than part of the voucher that created it.
/// Its lifetime is longer than that voucher's: an invoice raised in April may be
/// settled by three receipts across two financial years, and each of those
/// allocations must be recorded without reopening the original document.
/// </para>
/// </remarks>
public sealed class Bill : AggregateRoot<BillId>, IFirmScoped, IAuditable
{
    private readonly List<BillAllocation> _allocations = [];

    private Bill(
        BillId id,
        TenantId tenantId,
        FirmId firmId,
        LedgerId ledgerId,
        VoucherId originVoucherId,
        BillType type,
        string billNumber,
        DateOnly billDate,
        DateOnly dueDate,
        Money originalAmount)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        LedgerId = ledgerId;
        OriginVoucherId = originVoucherId;
        Type = type;
        BillNumber = billNumber;
        BillDate = billDate;
        DueDate = dueDate;
        OriginalAmount = originalAmount;
        SettledAmount = Money.Zero(originalAmount.Currency);
        Status = BillStatus.Open;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Bill() => BillNumber = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the party this bill is owed by or to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets the voucher that raised this bill.</summary>
    public VoucherId OriginVoucherId { get; private set; }

    /// <summary>Gets whether the bill is receivable or payable.</summary>
    public BillType Type { get; private set; }

    /// <summary>Gets the reference the party knows it by - usually an invoice number.</summary>
    public string BillNumber { get; private set; }

    /// <summary>Gets the date the bill was raised.</summary>
    public DateOnly BillDate { get; private set; }

    /// <summary>
    /// Gets the date payment falls due, from the party's credit terms.
    /// </summary>
    /// <remarks>
    /// Stored rather than derived. Credit terms change, and a bill raised under
    /// 30-day terms does not become overdue sooner because the customer was later
    /// moved to 15 days.
    /// </remarks>
    public DateOnly DueDate { get; private set; }

    /// <summary>Gets the amount the bill was raised for.</summary>
    public Money OriginalAmount { get; private set; }

    /// <summary>Gets how much has been allocated against it so far.</summary>
    public Money SettledAmount { get; private set; }

    /// <summary>Gets how much remains outstanding.</summary>
    public Money OutstandingAmount => OriginalAmount - SettledAmount;

    /// <summary>Gets the settlement status.</summary>
    public BillStatus Status { get; private set; }

    /// <summary>Gets the allocations made against this bill.</summary>
    public IReadOnlyCollection<BillAllocation> Allocations => _allocations.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Raises a bill.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="ledgerId">The party owing or owed.</param>
    /// <param name="originVoucherId">The voucher raising it.</param>
    /// <param name="type">Receivable or payable.</param>
    /// <param name="billNumber">The reference the party knows it by.</param>
    /// <param name="billDate">The date it was raised.</param>
    /// <param name="creditDays">
    /// The party's credit terms, used to compute the due date. Zero means due
    /// immediately.
    /// </param>
    /// <param name="originalAmount">The amount. Must be positive.</param>
    /// <returns>The bill, or a validation failure.</returns>
    public static Result<Bill> Raise(
        TenantId tenantId,
        FirmId firmId,
        LedgerId ledgerId,
        VoucherId originVoucherId,
        BillType type,
        string billNumber,
        DateOnly billDate,
        int creditDays,
        Money originalAmount)
    {
        if (string.IsNullOrWhiteSpace(billNumber))
        {
            return Result.Failure<Bill>(Error.Validation(
                "Bill.NumberRequired", "A bill reference is required."));
        }

        if (billNumber.Trim().Length > 50)
        {
            return Result.Failure<Bill>(Error.Validation(
                "Bill.NumberTooLong", "A bill reference cannot exceed 50 characters."));
        }

        // A bill for nothing is not a bill, and a negative one would make the
        // outstanding arithmetic meaningless. A credit note is a bill of the
        // opposite type, not a negative one.
        if (!originalAmount.IsPositive)
        {
            return Result.Failure<Bill>(Error.Validation(
                "Bill.AmountNotPositive",
                "A bill must be raised for a positive amount. Record a credit note as " +
                "a bill of the opposite type."));
        }

        if (creditDays < 0)
        {
            return Result.Failure<Bill>(Error.Validation(
                "Bill.CreditDaysNegative", "Credit days cannot be negative."));
        }

        return Result.Success(new Bill(
            BillId.NewId(),
            tenantId,
            firmId,
            ledgerId,
            originVoucherId,
            type,
            billNumber.Trim(),
            billDate,
            billDate.AddDays(creditDays),
            originalAmount));
    }

    /// <summary>Determines whether the bill is overdue as at a date.</summary>
    /// <param name="asAt">The date to assess.</param>
    /// <returns><see langword="true"/> when outstanding and past its due date.</returns>
    public bool IsOverdueAt(DateOnly asAt) =>
        Status != BillStatus.Settled && asAt > DueDate;

    /// <summary>
    /// Returns how many days past due the bill is, or zero when not yet due.
    /// </summary>
    /// <param name="asAt">The date to assess.</param>
    /// <returns>The days overdue, never negative.</returns>
    /// <remarks>
    /// Counted from the due date, not the bill date. Aging a bill from when it was
    /// raised would report every invoice as overdue the moment it is issued, which
    /// is the classic way to make an aging report useless.
    /// </remarks>
    public int DaysOverdueAt(DateOnly asAt)
    {
        int days = asAt.DayNumber - DueDate.DayNumber;
        return days > 0 ? days : 0;
    }

    /// <summary>Allocates a receipt or payment against this bill.</summary>
    /// <param name="voucherId">The settling voucher.</param>
    /// <param name="amount">The amount to allocate. Must be positive.</param>
    /// <param name="allocatedOn">The date of the settling document.</param>
    /// <returns>Success, or the reason the allocation was refused.</returns>
    /// <remarks>
    /// Over-allocation is refused rather than silently capped. A receipt exceeding
    /// what a bill is owed means the operator has picked the wrong bill or typed
    /// the wrong figure, and quietly absorbing the difference would hide the
    /// mistake and leave the party's balance wrong.
    /// </remarks>
    public Result Allocate(VoucherId voucherId, Money amount, DateOnly allocatedOn)
    {
        if (Status == BillStatus.Settled)
        {
            return Result.Failure(Error.BusinessRule(
                "Bill.AlreadySettled",
                $"Bill '{BillNumber}' is fully settled and cannot take further allocations."));
        }

        if (amount.Currency != OriginalAmount.Currency)
        {
            return Result.Failure(Error.Validation(
                "Bill.CurrencyMismatch",
                $"Bill '{BillNumber}' is in {OriginalAmount.Currency}; an allocation in " +
                $"{amount.Currency} must be converted first."));
        }

        if (!amount.IsPositive)
        {
            return Result.Failure(Error.Validation(
                "Bill.AllocationNotPositive", "An allocation must be for a positive amount."));
        }

        if (amount > OutstandingAmount)
        {
            return Result.Failure(Error.BusinessRule(
                "Bill.OverAllocated",
                $"Cannot allocate {amount} to bill '{BillNumber}': only " +
                $"{OutstandingAmount} is outstanding."));
        }

        _allocations.Add(new BillAllocation(Id, voucherId, amount, allocatedOn, TenantId));
        SettledAmount += amount;
        UpdateStatus();

        if (Status == BillStatus.Settled)
        {
            Raise(new BillSettled(Id, TenantId, FirmId, LedgerId, BillNumber, allocatedOn));
        }

        return Result.Success();
    }

    /// <summary>Removes every allocation made by a voucher.</summary>
    /// <param name="voucherId">The voucher being cancelled or reversed.</param>
    /// <returns>The amount released back to outstanding.</returns>
    /// <remarks>
    /// Called when a settling voucher is cancelled. Without it a cancelled receipt
    /// would leave its bills showing as paid, and the party's outstanding would
    /// understate what they actually owe - a discrepancy that surfaces only when
    /// somebody chases a payment that was never really made.
    /// </remarks>
    public Money ReleaseAllocationsFrom(VoucherId voucherId)
    {
        Money released = Money.Zero(OriginalAmount.Currency);

        foreach (BillAllocation allocation in _allocations.Where(a => a.VoucherId == voucherId))
        {
            released += allocation.Amount;
        }

        if (released.IsZero)
        {
            return released;
        }

        _allocations.RemoveAll(a => a.VoucherId == voucherId);
        SettledAmount -= released;
        UpdateStatus();

        return released;
    }

    private void UpdateStatus()
    {
        if (OutstandingAmount.IsZero)
        {
            Status = BillStatus.Settled;
            return;
        }

        Status = SettledAmount.IsZero ? BillStatus.Open : BillStatus.PartiallySettled;
    }
}

/// <summary>One settlement against a bill.</summary>
public sealed class BillAllocation : ITenantScoped
{
    /// <summary>Initialises a new instance of the <see cref="BillAllocation"/> class.</summary>
    /// <param name="billId">The bill settled.</param>
    /// <param name="voucherId">The settling voucher.</param>
    /// <param name="amount">The amount allocated.</param>
    /// <param name="allocatedOn">The date of the settling document.</param>
    /// <param name="tenantId">The owning tenant.</param>
    internal BillAllocation(
        BillId billId,
        VoucherId voucherId,
        Money amount,
        DateOnly allocatedOn,
        TenantId tenantId)
    {
        Id = BillAllocationId.NewId();
        BillId = billId;
        VoucherId = voucherId;
        Amount = amount;
        AllocatedOn = allocatedOn;
        TenantId = tenantId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private BillAllocation()
    {
    }

    /// <summary>Gets this allocation's identity.</summary>
    public BillAllocationId Id { get; private set; }

    /// <summary>Gets the bill settled.</summary>
    public BillId BillId { get; private set; }

    /// <summary>Gets the settling voucher.</summary>
    public VoucherId VoucherId { get; private set; }

    /// <summary>Gets the amount allocated.</summary>
    public Money Amount { get; private set; }

    /// <summary>Gets the date of the settling document.</summary>
    public DateOnly AllocatedOn { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }
}

/// <summary>Raised when a bill becomes fully settled.</summary>
/// <param name="BillId">The bill.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="LedgerId">The party.</param>
/// <param name="BillNumber">The bill reference.</param>
/// <param name="SettledOn">The date it was settled.</param>
/// <remarks>
/// Consumed by the notification module, so a credit controller chasing a debt
/// stops chasing it the moment it is paid.
/// </remarks>
public sealed record BillSettled(
    BillId BillId,
    TenantId TenantId,
    FirmId FirmId,
    LedgerId LedgerId,
    string BillNumber,
    DateOnly SettledOn) : DomainEvent;
