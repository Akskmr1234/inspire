using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Accounting;

/// <summary>
/// One posting within a voucher: a ledger, a side, and an amount.
/// </summary>
/// <remarks>
/// Part of the <see cref="Voucher"/> aggregate, not an aggregate of its own, so it
/// can only be created and changed through the voucher that owns it. That is what
/// keeps the balance invariant enforceable - a line reachable independently could
/// be altered without anything re-checking that debits still equal credits.
/// </remarks>
public sealed class VoucherLine : Entity<VoucherLineId>, ITenantScoped
{
    internal VoucherLine(
        VoucherLineId id,
        TenantId tenantId,
        VoucherId voucherId,
        LedgerId ledgerId,
        EntrySide side,
        Money amount,
        int lineNumber,
        string? narration)
        : base(id)
    {
        TenantId = tenantId;
        VoucherId = voucherId;
        LedgerId = ledgerId;
        Side = side;
        Amount = amount;
        BaseAmount = amount;
        LineNumber = lineNumber;
        Narration = narration;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private VoucherLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the owning voucher.</summary>
    public VoucherId VoucherId { get; private set; }

    /// <summary>Gets the ledger this line posts against.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets whether this line debits or credits the ledger.</summary>
    public EntrySide Side { get; private set; }

    /// <summary>
    /// Gets the amount in the voucher's entry currency. Always positive; the
    /// direction is carried by <see cref="Side"/>.
    /// </summary>
    public Money Amount { get; private set; }

    /// <summary>
    /// Gets the amount in the firm's base currency, assigned when the voucher is
    /// posted.
    /// </summary>
    /// <remarks>
    /// Derived by allocating the converted voucher total across the lines of each
    /// side rather than by converting this line on its own - see
    /// <see cref="Voucher.Post"/>. That is what guarantees base-currency debits and
    /// credits still agree after rounding.
    /// <para>
    /// Every balance, trial balance, and financial statement is built from this
    /// figure, because the books are kept in the base currency.
    /// </para>
    /// </remarks>
    public Money BaseAmount { get; private set; }

    /// <summary>Gets the line's position on the voucher, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the line-level narration.</summary>
    public string? Narration { get; private set; }

    /// <summary>
    /// Gets the amount as a debit, or zero when this is a credit line.
    /// </summary>
    /// <remarks>
    /// The entry grid and the printed voucher both present separate Debit Amount
    /// and Credit Amount columns. Exposing them as projections keeps that
    /// presentation available without storing two nullable columns, one of which is
    /// always empty - a shape that invites a row with both populated.
    /// </remarks>
    public Money DebitAmount =>
        Side == EntrySide.Debit ? Amount : Money.Zero(Amount.Currency);

    /// <summary>Gets the amount as a credit, or zero when this is a debit line.</summary>
    public Money CreditAmount =>
        Side == EntrySide.Credit ? Amount : Money.Zero(Amount.Currency);

    /// <summary>
    /// Gets the amount signed in debit-positive terms: positive for a debit,
    /// negative for a credit.
    /// </summary>
    /// <remarks>
    /// Summing this across every posted line of a firm must give exactly zero.
    /// That single property is the trial balance.
    /// </remarks>
    public decimal SignedBaseAmount => BaseAmount.Amount * Side.Sign();

    /// <summary>Records the base-currency amount. Called by the owning voucher on posting.</summary>
    /// <param name="baseAmount">The converted amount.</param>
    internal void AssignBaseAmount(Money baseAmount) => BaseAmount = baseAmount;

    /// <summary>Renumbers the line after a sibling is removed.</summary>
    /// <param name="lineNumber">The new position.</param>
    internal void SetLineNumber(int lineNumber) => LineNumber = lineNumber;
}

/// <summary>Raised when a voucher is posted to the ledgers.</summary>
/// <param name="VoucherId">The voucher.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="BranchId">The originating branch.</param>
/// <param name="Type">The kind of voucher.</param>
/// <param name="Number">The document number.</param>
/// <param name="Date">The document date.</param>
/// <param name="Total">The voucher total, in the entry currency.</param>
public sealed record VoucherPosted(
    VoucherId VoucherId,
    TenantId TenantId,
    FirmId FirmId,
    BranchId BranchId,
    VoucherType Type,
    string Number,
    DateOnly Date,
    Money Total) : DomainEvent;

/// <summary>Raised when a posted voucher is cancelled.</summary>
/// <param name="VoucherId">The voucher.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="Number">The document number.</param>
/// <param name="Reason">Why it was cancelled.</param>
public sealed record VoucherCancelled(
    VoucherId VoucherId,
    TenantId TenantId,
    string Number,
    string Reason) : DomainEvent;
