using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Accounting;

/// <summary>Which way a cheque runs.</summary>
public enum ChequeDirection
{
    /// <summary>Taken in from a customer, to be banked.</summary>
    Received = 1,

    /// <summary>Written to a supplier, to be presented against the firm's account.</summary>
    Issued = 2,
}

/// <summary>Where a cheque stands in its lifecycle.</summary>
public enum ChequeStatus
{
    /// <summary>
    /// In hand. A post-dated cheque waits here until its date; a current one waits
    /// until somebody takes it to the bank.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// In the banking system - paid in, or presented by the payee - and not yet
    /// resolved.
    /// </summary>
    Deposited = 2,

    /// <summary>The funds have moved. Terminal.</summary>
    Cleared = 3,

    /// <summary>Dishonoured by the bank. Terminal.</summary>
    Bounced = 4,

    /// <summary>Payment stopped by the drawer before it cleared. Terminal.</summary>
    Stopped = 5,

    /// <summary>Voided before it ever reached a bank. Terminal.</summary>
    Cancelled = 6,
}

/// <summary>
/// One cheque, tracked from the moment it changes hands to the moment it clears,
/// bounces, or is stopped.
/// </summary>
/// <remarks>
/// <para>
/// The specification puts cheque lifecycle in Phase 1 and names three reports built
/// on it: the PDC report, the PDC calendar, and the cheque register. All three exist
/// because a cheque is not the same thing as the money it promises. Between taking
/// one in and its clearing there is a period - often weeks, for a post-dated cheque -
/// in which the firm holds a claim that may yet fail, and no ledger balance
/// distinguishes that from cash in the bank.
/// </para>
/// <para>
/// A cheque is its own aggregate rather than a detail of the voucher that recorded
/// it, for the same reason a bill is: its lifetime is longer. A cheque taken in
/// during March may clear in June, bounce in July, and be replaced in August, and
/// each of those is a separate event that must be recorded without reopening the
/// original receipt.
/// </para>
/// <para>
/// Whether a cheque is post-dated is derived from its instrument date rather than
/// stored. A stored flag would be true the day the cheque was taken in and still
/// true the day it matured, so every report reading it would have to correct for
/// that - and one of them eventually would not.
/// </para>
/// </remarks>
public sealed class Cheque : AggregateRoot<ChequeId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a cheque number may be.</summary>
    public const int MaximumNumberLength = 30;

    /// <summary>The longest a drawn-on bank name may be.</summary>
    public const int MaximumBankNameLength = 100;

    /// <summary>The longest a closure reason may be.</summary>
    public const int MaximumReasonLength = 500;

    private Cheque(
        ChequeId id,
        TenantId tenantId,
        FirmId firmId,
        ChequeDirection direction,
        LedgerId partyLedgerId,
        VoucherId originVoucherId,
        string chequeNumber,
        DateOnly instrumentDate,
        DateOnly recordedOn,
        Money amount,
        LedgerId? bankLedgerId,
        string? drawnOnBank)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Direction = direction;
        PartyLedgerId = partyLedgerId;
        OriginVoucherId = originVoucherId;
        ChequeNumber = chequeNumber;
        InstrumentDate = instrumentDate;
        RecordedOn = recordedOn;
        Amount = amount;
        BankLedgerId = bankLedgerId;
        DrawnOnBank = drawnOnBank;
        Status = ChequeStatus.Pending;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Cheque() => ChequeNumber = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets whether the cheque was taken in or written out.</summary>
    public ChequeDirection Direction { get; private set; }

    /// <summary>Gets the party the cheque was taken from, or written to.</summary>
    public LedgerId PartyLedgerId { get; private set; }

    /// <summary>Gets the voucher that brought the cheque into the register.</summary>
    public VoucherId OriginVoucherId { get; private set; }

    /// <summary>Gets the voucher that posted the clearance, once it cleared.</summary>
    /// <remarks>
    /// Held so a cleared cheque can be traced to the bank movement it produced. The
    /// receipt and the clearance are separate postings - the first moves the debt to
    /// cheques in hand, the second moves it to the bank - and a register that could
    /// not link them would leave a reader to match them by amount and date.
    /// </remarks>
    public VoucherId? ClearingVoucherId { get; private set; }

    /// <summary>Gets the voucher that reversed the receipt, once it bounced.</summary>
    /// <remarks>
    /// Supplied by the operator with the bounce rather than raised here. A dishonoured
    /// cheque has to come back out of whichever account the firm parked it in - cheques
    /// in hand, an undeposited-funds account, the customer's own ledger - and the bank's
    /// charge for it goes somewhere else again. Neither is derivable from anything this
    /// system holds, so the voucher is written by whoever knows the chart and named
    /// here, which is the same arrangement <see cref="ClearingVoucherId"/> uses for the
    /// clearance. Null means the reversal is still owed.
    /// </remarks>
    public VoucherId? ReversalVoucherId { get; private set; }

    /// <summary>Gets the number printed on the cheque.</summary>
    public string ChequeNumber { get; private set; }

    /// <summary>
    /// Gets the date written on the face of the cheque - the date it may be banked.
    /// </summary>
    public DateOnly InstrumentDate { get; private set; }

    /// <summary>Gets the date the cheque changed hands.</summary>
    /// <remarks>
    /// Distinct from <see cref="InstrumentDate"/>, and the difference between the
    /// two is what makes a cheque post-dated. Both are needed: the register is read
    /// by when a cheque was taken in, and the calendar by when it falls due.
    /// </remarks>
    public DateOnly RecordedOn { get; private set; }

    /// <summary>Gets the amount the cheque is drawn for.</summary>
    public Money Amount { get; private set; }

    /// <summary>
    /// Gets the firm's own bank account: the one an issued cheque is drawn on, or
    /// the one a received cheque was paid into.
    /// </summary>
    /// <remarks>
    /// Known from the start for an issued cheque and only at deposit for a received
    /// one, which is why it is nullable. A received cheque sits in hand until
    /// somebody decides which account to bank it in.
    /// </remarks>
    public LedgerId? BankLedgerId { get; private set; }

    /// <summary>Gets the bank named on the face of a received cheque.</summary>
    /// <remarks>
    /// Free text rather than a ledger, because it is the payer's bank and the firm
    /// keeps no account for it. It is on the register because it is how a bounced
    /// cheque is chased.
    /// </remarks>
    public string? DrawnOnBank { get; private set; }

    /// <summary>Gets the lifecycle status.</summary>
    public ChequeStatus Status { get; private set; }

    /// <summary>Gets the date the cheque entered the banking system.</summary>
    public DateOnly? DepositedOn { get; private set; }

    /// <summary>Gets the date the cheque reached its terminal state.</summary>
    /// <remarks>
    /// One date rather than one per outcome. Which outcome it was is
    /// <see cref="Status"/>; four mutually exclusive nullable dates would be four
    /// chances to set the wrong one.
    /// </remarks>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Gets why the cheque bounced, was stopped, or was cancelled.</summary>
    public string? ClosureReason { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets the date the cheque cleared, if it did.</summary>
    public DateOnly? ClearedOn => Status == ChequeStatus.Cleared ? ClosedOn : null;

    /// <summary>Gets whether the cheque has reached a state it cannot leave.</summary>
    public bool IsClosed => Status
        is ChequeStatus.Cleared
        or ChequeStatus.Bounced
        or ChequeStatus.Stopped
        or ChequeStatus.Cancelled;

    /// <summary>Gets whether the cheque is still an open claim or obligation.</summary>
    public bool IsOutstanding => !IsClosed;

    /// <summary>Records a cheque taken in or written out.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="direction">Received or issued.</param>
    /// <param name="partyLedgerId">The party it came from, or goes to.</param>
    /// <param name="originVoucherId">The voucher recording it.</param>
    /// <param name="chequeNumber">The number printed on the cheque.</param>
    /// <param name="instrumentDate">The date written on its face.</param>
    /// <param name="recordedOn">The date it changed hands.</param>
    /// <param name="amount">The amount. Must be positive.</param>
    /// <param name="bankLedgerId">
    /// The firm's account. Required for an issued cheque, which is drawn on a known
    /// account; omitted for a received one, whose account is chosen at deposit.
    /// </param>
    /// <param name="drawnOnBank">The bank named on a received cheque, if known.</param>
    /// <returns>The cheque, or a validation failure.</returns>
    public static Result<Cheque> Record(
        TenantId tenantId,
        FirmId firmId,
        ChequeDirection direction,
        LedgerId partyLedgerId,
        VoucherId originVoucherId,
        string chequeNumber,
        DateOnly instrumentDate,
        DateOnly recordedOn,
        Money amount,
        LedgerId? bankLedgerId = null,
        string? drawnOnBank = null)
    {
        if (!Enum.IsDefined(direction))
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.UnknownDirection", $"'{direction}' is not a recognised cheque direction."));
        }

        if (string.IsNullOrWhiteSpace(chequeNumber))
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.NumberRequired", "A cheque number is required."));
        }

        if (chequeNumber.Trim().Length > MaximumNumberLength)
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.NumberTooLong",
                $"A cheque number cannot exceed {MaximumNumberLength} characters."));
        }

        // A cheque for nothing is not a cheque, and a negative one would make the
        // register's totals meaningless. A refund is a cheque in the other
        // direction.
        if (!amount.IsPositive)
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.AmountNotPositive", "A cheque must be for a positive amount."));
        }

        // An issued cheque is drawn on a specific account of the firm's, and the
        // register has to say which - a bank reconciliation that could not tell
        // which account a cheque would hit would be unusable.
        if (direction == ChequeDirection.Issued && bankLedgerId is null)
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.BankAccountRequired",
                "An issued cheque must name the account it is drawn on."));
        }

        if (drawnOnBank is not null && drawnOnBank.Trim().Length > MaximumBankNameLength)
        {
            return Result.Failure<Cheque>(Error.Validation(
                "Cheque.BankNameTooLong",
                $"A bank name cannot exceed {MaximumBankNameLength} characters."));
        }

        return Result.Success(new Cheque(
            ChequeId.NewId(),
            tenantId,
            firmId,
            direction,
            partyLedgerId,
            originVoucherId,
            chequeNumber.Trim(),
            instrumentDate,
            recordedOn,
            amount,
            bankLedgerId,
            string.IsNullOrWhiteSpace(drawnOnBank) ? null : drawnOnBank.Trim()));
    }

    /// <summary>Determines whether the cheque is post-dated as at a date.</summary>
    /// <param name="asAt">The date to assess.</param>
    /// <returns><see langword="true"/> when its date has not yet arrived.</returns>
    /// <remarks>
    /// Derived rather than stored. A stored flag would be set when the cheque was
    /// taken in and still set the day it matured, leaving every report to correct
    /// for it - and one of them eventually would not.
    /// </remarks>
    public bool IsPostDatedAt(DateOnly asAt) => InstrumentDate > asAt;

    /// <summary>
    /// Determines whether the cheque is in hand and ready to be banked as at a date.
    /// </summary>
    /// <param name="asAt">The date to assess.</param>
    /// <returns><see langword="true"/> when it is pending and its date has arrived.</returns>
    /// <remarks>
    /// What the PDC calendar is read for: which cheques become bankable this week.
    /// </remarks>
    public bool IsDueAt(DateOnly asAt) =>
        Status == ChequeStatus.Pending && InstrumentDate <= asAt;

    /// <summary>
    /// Records the cheque entering the banking system - paid in, for a received
    /// cheque, or presented by the payee, for an issued one.
    /// </summary>
    /// <param name="bankLedgerId">The firm's account it went through.</param>
    /// <param name="depositedOn">The date it was banked or presented.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Banking a cheque before its date is refused rather than allowed. That is the
    /// whole meaning of a post-dated cheque: presented early it is returned, and the
    /// firm pays a charge for the privilege. Refusing at the point somebody tries is
    /// cheaper than reporting it afterwards.
    /// </remarks>
    public Result Deposit(LedgerId bankLedgerId, DateOnly depositedOn)
    {
        if (Status != ChequeStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.NotPending",
                $"Cheque '{ChequeNumber}' is {Describe(Status)} and cannot be banked."));
        }

        if (depositedOn < InstrumentDate)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.BankedBeforeItsDate",
                $"Cheque '{ChequeNumber}' is dated {InstrumentDate:yyyy-MM-dd} and cannot " +
                $"be banked on {depositedOn:yyyy-MM-dd}. A post-dated cheque presented " +
                $"early is returned."));
        }

        // An issued cheque is drawn on one account and can be presented against no
        // other. Accepting a different one would put the payment through the wrong
        // bank reconciliation.
        if (Direction == ChequeDirection.Issued
            && BankLedgerId is { } drawnOn
            && drawnOn != bankLedgerId)
        {
            return Result.Failure(Error.Validation(
                "Cheque.WrongDrawnOnAccount",
                $"Cheque '{ChequeNumber}' is drawn on a different account and cannot be " +
                $"presented against this one."));
        }

        BankLedgerId = bankLedgerId;
        DepositedOn = depositedOn;
        Status = ChequeStatus.Deposited;

        return Result.Success();
    }

    /// <summary>Records the cheque clearing.</summary>
    /// <param name="clearedOn">The date the funds moved.</param>
    /// <param name="clearingVoucherId">The voucher posting the bank movement.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Clear(DateOnly clearedOn, VoucherId clearingVoucherId)
    {
        if (Status != ChequeStatus.Deposited)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.NotDeposited",
                $"Cheque '{ChequeNumber}' is {Describe(Status)} and cannot clear. Bank it " +
                $"first."));
        }

        if (DepositedOn is { } banked && clearedOn < banked)
        {
            return Result.Failure(Error.Validation(
                "Cheque.ClearedBeforeItWasBanked",
                $"Cheque '{ChequeNumber}' was banked on {banked:yyyy-MM-dd} and cannot have " +
                $"cleared on {clearedOn:yyyy-MM-dd}."));
        }

        Status = ChequeStatus.Cleared;
        ClosedOn = clearedOn;
        ClearingVoucherId = clearingVoucherId;

        Raise(new ChequeCleared(
            Id, TenantId, FirmId, PartyLedgerId, Direction, ChequeNumber, Amount, clearedOn));

        return Result.Success();
    }

    /// <summary>Records the cheque being dishonoured.</summary>
    /// <param name="reason">The bank's reason for returning it.</param>
    /// <param name="bouncedOn">The date it was returned.</param>
    /// <param name="reversalVoucherId">
    /// The voucher taking the receipt back out of the books, where the operator has
    /// written one. Null leaves the reversal owed, and the caller is told so.
    /// </param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The reason is required because the bank always gives one and it decides what
    /// happens next: a cheque returned for insufficient funds is re-presented, one
    /// returned for a signature mismatch is replaced, and one returned on a closed
    /// account is a collections matter. Recording "bounced" alone loses that.
    /// <para>
    /// A bounced cheque is terminal here. Re-presenting it is recorded as a new
    /// cheque, which is also what the bank statement shows: the second presentation
    /// is a separate event with its own charges and its own outcome.
    /// </para>
    /// </remarks>
    public Result Bounce(string reason, DateOnly bouncedOn, VoucherId? reversalVoucherId = null)
    {
        if (Status != ChequeStatus.Deposited)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.NotDeposited",
                $"Cheque '{ChequeNumber}' is {Describe(Status)} and cannot bounce."));
        }

        Result<string> stated = RequireReason(reason, "Cheque.BounceReasonRequired");

        if (stated.IsFailure)
        {
            return Result.Failure(stated.Error);
        }

        Status = ChequeStatus.Bounced;
        ClosedOn = bouncedOn;
        ClosureReason = stated.Value;
        ReversalVoucherId = reversalVoucherId;

        Raise(new ChequeBounced(
            Id, TenantId, FirmId, PartyLedgerId, Direction, ChequeNumber, Amount,
            bouncedOn, stated.Value));

        return Result.Success();
    }

    /// <summary>Attaches the reversing voucher to a cheque that has already bounced.</summary>
    /// <param name="reversalVoucherId">The voucher taking the receipt back out.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The ordinary sequence, in practice. The bank returns a cheque and the cashier
    /// records it that morning; the reversing journal is written afterwards, by
    /// somebody who knows which account it comes back out of. Forcing the two into one
    /// step would mean either holding the bounce until the accountant is free - leaving
    /// bills settled that are not - or posting on a guess.
    /// <para>
    /// Once attached it cannot be swapped. A cheque pointing at one reversal on Monday
    /// and another on Tuesday is a register that cannot be relied on to explain itself;
    /// a wrong voucher is corrected the way any other wrong posting is, by reversing it
    /// in the books rather than by rewriting what this says happened.
    /// </para>
    /// </remarks>
    public Result RecordReversal(VoucherId reversalVoucherId)
    {
        if (Status != ChequeStatus.Bounced)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.NotBounced",
                $"Cheque '{ChequeNumber}' is {Describe(Status)}, so there is no bounce to "
                + "reverse."));
        }

        if (ReversalVoucherId is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.AlreadyReversed",
                $"Cheque '{ChequeNumber}' already names the voucher that reversed it."));
        }

        ReversalVoucherId = reversalVoucherId;

        return Result.Success();
    }

    /// <summary>Records payment being stopped on a cheque the firm issued.</summary>
    /// <param name="reason">Why payment was stopped.</param>
    /// <param name="stoppedOn">The date the instruction took effect.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Issued cheques only. Only the drawer can stop a cheque, and for a cheque the
    /// firm received the drawer is the customer - from the firm's side that arrives
    /// as a bounce, from the bank, not as an instruction the firm gave. Allowing
    /// both to be recorded the same way would lose the distinction between a payment
    /// the firm withdrew and one a customer reneged on.
    /// </remarks>
    public Result Stop(string reason, DateOnly stoppedOn)
    {
        if (Direction != ChequeDirection.Issued)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.OnlyIssuedCanBeStopped",
                $"Cheque '{ChequeNumber}' was received, not issued. A cheque the drawer " +
                $"stops comes back as a bounce."));
        }

        if (IsClosed)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.AlreadyClosed",
                $"Cheque '{ChequeNumber}' is {Describe(Status)} and payment can no longer " +
                $"be stopped."));
        }

        Result<string> stated = RequireReason(reason, "Cheque.StopReasonRequired");

        if (stated.IsFailure)
        {
            return Result.Failure(stated.Error);
        }

        Status = ChequeStatus.Stopped;
        ClosedOn = stoppedOn;
        ClosureReason = stated.Value;

        return Result.Success();
    }

    /// <summary>Voids a cheque that never reached a bank.</summary>
    /// <param name="reason">Why it was voided.</param>
    /// <param name="cancelledOn">The date it was voided.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Only from <see cref="ChequeStatus.Pending"/>. Once a cheque is in the banking
    /// system its outcome is the bank's to report, and cancelling it in the register
    /// would leave the books saying nothing happened while the statement says
    /// otherwise.
    /// </remarks>
    public Result Cancel(string reason, DateOnly cancelledOn)
    {
        if (Status != ChequeStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.NotPending",
                $"Cheque '{ChequeNumber}' is {Describe(Status)} and can no longer be " +
                $"cancelled."));
        }

        Result<string> stated = RequireReason(reason, "Cheque.CancellationReasonRequired");

        if (stated.IsFailure)
        {
            return Result.Failure(stated.Error);
        }

        Status = ChequeStatus.Cancelled;
        ClosedOn = cancelledOn;
        ClosureReason = stated.Value;

        return Result.Success();
    }

    /// <summary>Validates and trims a stated reason.</summary>
    /// <param name="reason">The reason supplied.</param>
    /// <param name="code">The error code to report when it is missing.</param>
    /// <returns>The trimmed reason, or a validation failure.</returns>
    private static Result<string> RequireReason(string reason, string code)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<string>(Error.Validation(
                code, "A reason must be given, and recorded against the cheque."));
        }

        return reason.Trim().Length > MaximumReasonLength
            ? Result.Failure<string>(Error.Validation(
                "Cheque.ReasonTooLong",
                $"A reason cannot exceed {MaximumReasonLength} characters."))
            : Result.Success(reason.Trim());
    }

    /// <summary>Describes a status as it should read in a message.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The wording to put in front of a user.</returns>
    private static string Describe(ChequeStatus status) => status switch
    {
        ChequeStatus.Pending => "in hand",
        ChequeStatus.Deposited => "already with the bank",
        ChequeStatus.Cleared => "already cleared",
        ChequeStatus.Bounced => "bounced",
        ChequeStatus.Stopped => "stopped",
        ChequeStatus.Cancelled => "cancelled",
        _ => status.ToString(),
    };
}

/// <summary>Raised when a cheque clears.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="PartyLedgerId">The party.</param>
/// <param name="Direction">Received or issued.</param>
/// <param name="ChequeNumber">The cheque number.</param>
/// <param name="Amount">The amount.</param>
/// <param name="ClearedOn">The date the funds moved.</param>
public sealed record ChequeCleared(
    ChequeId ChequeId,
    TenantId TenantId,
    FirmId FirmId,
    LedgerId PartyLedgerId,
    ChequeDirection Direction,
    string ChequeNumber,
    Money Amount,
    DateOnly ClearedOn) : DomainEvent;

/// <summary>Raised when a cheque is dishonoured.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="FirmId">The owning firm.</param>
/// <param name="PartyLedgerId">The party.</param>
/// <param name="Direction">Received or issued.</param>
/// <param name="ChequeNumber">The cheque number.</param>
/// <param name="Amount">The amount.</param>
/// <param name="BouncedOn">The date it was returned.</param>
/// <param name="Reason">The bank's reason for returning it.</param>
/// <remarks>
/// The consequential one. A receipt settled by a cheque that later bounces has to be
/// undone: the bills it closed are owed again, and the customer's balance was
/// overstated in the meantime. Anything that acted on the settlement - a credit
/// controller who stopped chasing, a credit limit that was freed - needs telling.
/// </remarks>
public sealed record ChequeBounced(
    ChequeId ChequeId,
    TenantId TenantId,
    FirmId FirmId,
    LedgerId PartyLedgerId,
    ChequeDirection Direction,
    string ChequeNumber,
    Money Amount,
    DateOnly BouncedOn,
    string Reason) : DomainEvent;
