using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using FluentValidation;

namespace ERP.Application.Accounting.Cheques;

/// <summary>Records a cheque being paid in, or presented against the firm's account.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="BankLedgerId">The firm's account it went through.</param>
/// <param name="DepositedOn">The date it was banked or presented.</param>
public sealed record DepositChequeCommand(
    Guid ChequeId,
    Guid BankLedgerId,
    DateOnly DepositedOn) : ICommand<ChequeStateResponse>, ITransactional;

/// <summary>Records a cheque clearing.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="ClearedOn">The date the funds moved.</param>
/// <param name="ClearingVoucherId">
/// The voucher posting the bank movement - for a received cheque, the entry moving
/// it out of cheques in hand and into the bank.
/// </param>
/// <remarks>
/// The posting is supplied rather than generated. Which control account a cleared
/// cheque moves out of is a firm's own choice of chart, and inventing one here would
/// mean guessing at somebody's books. This is also how bank reconciliation works in
/// practice: the statement is posted, then the cheques on it are marked off.
/// </remarks>
public sealed record ClearChequeCommand(
    Guid ChequeId,
    DateOnly ClearedOn,
    Guid ClearingVoucherId) : ICommand<ChequeStateResponse>, ITransactional;

/// <summary>Records a cheque being dishonoured.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="Reason">The bank's reason for returning it.</param>
/// <param name="BouncedOn">The date it was returned.</param>
/// <param name="ReversalVoucherId">
/// The voucher taking the receipt back out of the books, where one has already been
/// written. Optional, because the bank returns cheques to a cashier and the reversing
/// journal is usually written afterwards by somebody else; attach it later with
/// <see cref="RecordChequeReversalCommand"/>.
/// </param>
public sealed record BounceChequeCommand(
    Guid ChequeId,
    string Reason,
    DateOnly BouncedOn,
    Guid? ReversalVoucherId = null) : ICommand<BouncedChequeResponse>, ITransactional;

/// <summary>Names the voucher that reversed a bounced cheque's receipt.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="ReversalVoucherId">The voucher that took the receipt back out.</param>
/// <remarks>
/// The second half of a bounce, and the reason the response to the first says a
/// reversal is owed. Which account a dishonoured cheque comes back out of — cheques in
/// hand, undeposited funds, the customer's own ledger — and where the bank's charge
/// goes are a firm's own choice of chart. Nothing here can derive them, so the journal
/// is written by whoever knows the chart, and this records which one it was.
/// </remarks>
public sealed record RecordChequeReversalCommand(
    Guid ChequeId,
    Guid ReversalVoucherId) : ICommand<ChequeStateResponse>, ITransactional;

/// <summary>Records payment being stopped on a cheque the firm issued.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="Reason">Why payment was stopped.</param>
/// <param name="StoppedOn">The date the instruction took effect.</param>
public sealed record StopChequeCommand(
    Guid ChequeId,
    string Reason,
    DateOnly StoppedOn) : ICommand<ChequeStateResponse>, ITransactional;

/// <summary>Voids a cheque that never reached a bank.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="Reason">Why it was voided.</param>
/// <param name="CancelledOn">The date it was voided.</param>
public sealed record CancelChequeCommand(
    Guid ChequeId,
    string Reason,
    DateOnly CancelledOn) : ICommand<ChequeStateResponse>, ITransactional;

/// <summary>Where a cheque stands after a transition.</summary>
/// <param name="ChequeId">The cheque.</param>
/// <param name="ChequeNumber">The number printed on it.</param>
/// <param name="Status">Its new status.</param>
/// <param name="DepositedOn">When it entered the banking system, if it has.</param>
/// <param name="ClosedOn">When it reached its terminal state, if it has.</param>
public sealed record ChequeStateResponse(
    Guid ChequeId,
    string ChequeNumber,
    ChequeStatus Status,
    DateOnly? DepositedOn,
    DateOnly? ClosedOn);

/// <summary>What a bounce undid.</summary>
/// <param name="Cheque">The cheque's new state.</param>
/// <param name="BillsReopened">The bills whose settlement was released.</param>
/// <param name="AmountReopened">The total put back into outstanding.</param>
/// <param name="LedgerReversalRequired">
/// Whether a reversing journal is still owed. The bills are put back automatically;
/// the ledger postings are not, unless the bounce named the voucher that does it.
/// </param>
public sealed record BouncedChequeResponse(
    ChequeStateResponse Cheque,
    IReadOnlyList<ReopenedBill> BillsReopened,
    decimal AmountReopened,
    bool LedgerReversalRequired);

/// <summary>One bill a bounce put back into outstanding.</summary>
/// <param name="BillId">The bill.</param>
/// <param name="BillNumber">Its reference.</param>
/// <param name="AmountReleased">What was taken back off it.</param>
/// <param name="OutstandingAmount">What it is owed again, in total.</param>
public sealed record ReopenedBill(
    Guid BillId,
    string BillNumber,
    decimal AmountReleased,
    decimal OutstandingAmount);

/// <summary>Validates a <see cref="DepositChequeCommand"/>.</summary>
public sealed class DepositChequeCommandValidator : AbstractValidator<DepositChequeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="DepositChequeCommandValidator"/> class.</summary>
    public DepositChequeCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.BankLedgerId).NotEqual(Guid.Empty);
        RuleFor(c => c.DepositedOn).NotEqual(default(DateOnly));
    }
}

/// <summary>Validates a <see cref="ClearChequeCommand"/>.</summary>
public sealed class ClearChequeCommandValidator : AbstractValidator<ClearChequeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="ClearChequeCommandValidator"/> class.</summary>
    public ClearChequeCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.ClearedOn).NotEqual(default(DateOnly));
        RuleFor(c => c.ClearingVoucherId)
            .NotEqual(Guid.Empty)
            .WithMessage("The voucher posting the bank movement must be named.");
    }
}

/// <summary>Validates a <see cref="BounceChequeCommand"/>.</summary>
public sealed class BounceChequeCommandValidator : AbstractValidator<BounceChequeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="BounceChequeCommandValidator"/> class.</summary>
    public BounceChequeCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.BouncedOn).NotEqual(default(DateOnly));
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(Cheque.MaximumReasonLength);

        RuleFor(c => c.ReversalVoucherId)
            .NotEqual(Guid.Empty)
            .When(c => c.ReversalVoucherId is not null)
            .WithMessage("A reversing voucher must be named by its identifier.");
    }
}

/// <summary>Validates a <see cref="RecordChequeReversalCommand"/>.</summary>
public sealed class RecordChequeReversalCommandValidator
    : AbstractValidator<RecordChequeReversalCommand>
{
    /// <summary>Initialises a new instance of the <see cref="RecordChequeReversalCommandValidator"/> class.</summary>
    public RecordChequeReversalCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.ReversalVoucherId).NotEqual(Guid.Empty);
    }
}

/// <summary>Validates a <see cref="StopChequeCommand"/>.</summary>
public sealed class StopChequeCommandValidator : AbstractValidator<StopChequeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="StopChequeCommandValidator"/> class.</summary>
    public StopChequeCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.StoppedOn).NotEqual(default(DateOnly));
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(Cheque.MaximumReasonLength);
    }
}

/// <summary>Validates a <see cref="CancelChequeCommand"/>.</summary>
public sealed class CancelChequeCommandValidator : AbstractValidator<CancelChequeCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelChequeCommandValidator"/> class.</summary>
    public CancelChequeCommandValidator()
    {
        RuleFor(c => c.ChequeId).NotEqual(Guid.Empty);
        RuleFor(c => c.CancelledOn).NotEqual(default(DateOnly));
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(Cheque.MaximumReasonLength);
    }
}

/// <summary>
/// Loads a cheque and checks it belongs to the firm in scope, for every transition
/// handler.
/// </summary>
/// <remarks>
/// The firm check is not incidental. Tenant isolation permits reading a sibling
/// firm's cheque within the same tenant, and marking one cleared from the wrong set
/// of books would take a payment off a register nobody was looking at.
/// </remarks>
internal static class ChequeContext
{
    /// <summary>Resolves a cheque within the firm in scope.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="chequeId">The cheque wanted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cheque, or the reason it could not be resolved.</returns>
    internal static async Task<Result<Cheque>> ResolveAsync(
        IChequeRepository cheques,
        ITenantContext tenantContext,
        Guid chequeId,
        CancellationToken cancellationToken)
    {
        if (tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<Cheque>(Error.Forbidden(
                "Cheque.NoFirmSelected", "A firm must be selected to work with cheques."));
        }

        Cheque? cheque = await cheques.FindAsync(ChequeId.From(chequeId), cancellationToken);

        if (cheque is null)
        {
            return Result.Failure<Cheque>(Error.NotFound(
                "Cheque.NotFound", "That cheque does not exist."));
        }

        return cheque.FirmId == firmId
            ? Result.Success(cheque)
            : Result.Failure<Cheque>(Error.NotFound(
                "Cheque.NotFound", "That cheque does not exist in the selected firm."));
    }

    /// <summary>States a cheque's current position.</summary>
    /// <param name="cheque">The cheque.</param>
    /// <returns>The response describing it.</returns>
    internal static ChequeStateResponse StateOf(Cheque cheque) => new(
        cheque.Id.Value, cheque.ChequeNumber, cheque.Status,
        cheque.DepositedOn, cheque.ClosedOn);

    /// <summary>Checks that a voucher can stand as a cheque's reversing entry.</summary>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="cheque">The cheque being reversed.</param>
    /// <param name="voucherId">The voucher the operator named.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The voucher, or the reason it cannot be that.</returns>
    /// <remarks>
    /// Three checks, and deliberately not a fourth. It must exist in this firm, it must
    /// be in the books rather than a draft, and it must touch the party the cheque came
    /// from - otherwise it cannot be undoing that receipt, whatever it says in its
    /// narration. What is <em>not</em> checked is the amount: a reversal usually carries
    /// the bank's charge alongside the cheque, and a firm that nets a re-presentation
    /// against it is not making a mistake either. Requiring the figures to match would
    /// refuse correct entries in the name of a rule nobody stated.
    /// </remarks>
    internal static async Task<Result<Voucher>> ResolveReversalAsync(
        IVoucherRepository vouchers,
        Cheque cheque,
        Guid voucherId,
        CancellationToken cancellationToken)
    {
        Voucher? reversal = await vouchers.FindAsync(
            VoucherId.From(voucherId), cancellationToken);

        if (reversal is null || reversal.FirmId != cheque.FirmId)
        {
            return Result.Failure<Voucher>(Error.NotFound(
                "Cheque.ReversalVoucherNotFound", "No such voucher in the selected firm."));
        }

        if (reversal.Status != VoucherStatus.Posted)
        {
            return Result.Failure<Voucher>(Error.BusinessRule(
                "Cheque.ReversalVoucherNotPosted",
                $"Voucher '{reversal.Number}' is {reversal.Status} and is not in the books, "
                + "so it cannot reverse anything."));
        }

        return reversal.Lines.Any(line => line.LedgerId == cheque.PartyLedgerId)
            ? Result.Success(reversal)
            : Result.Failure<Voucher>(Error.BusinessRule(
                "Cheque.ReversalVoucherWrongParty",
                $"Voucher '{reversal.Number}' does not touch the party the cheque came "
                + "from, so it cannot be the entry that reverses it."));
    }
}

/// <summary>Handles <see cref="DepositChequeCommand"/>.</summary>
public sealed class DepositChequeCommandHandler
    : ICommandHandler<DepositChequeCommand, ChequeStateResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly ILedgerRepository _ledgers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="DepositChequeCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public DepositChequeCommandHandler(
        IChequeRepository cheques,
        ILedgerRepository ledgers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _ledgers = ledgers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeStateResponse>> Handle(
        DepositChequeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(found.Error);
        }

        Cheque cheque = found.Value;

        Ledger? bank = await _ledgers.FindAsync(
            LedgerId.From(request.BankLedgerId), cancellationToken);

        if (bank is null || bank.FirmId != cheque.FirmId)
        {
            return Result.Failure<ChequeStateResponse>(Error.NotFound(
                "Cheque.BankAccountNotFound", "No such account in the selected firm."));
        }

        // A cheque banked into the sales ledger would pass every other check and
        // make the bank reconciliation nonsense.
        if (bank.Kind != LedgerKind.Bank)
        {
            return Result.Failure<ChequeStateResponse>(Error.Validation(
                "Cheque.NotABankAccount", $"'{bank.Name}' is not a bank account."));
        }

        Result deposited = cheque.Deposit(bank.Id, request.DepositedOn);

        if (deposited.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(deposited.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ChequeContext.StateOf(cheque));
    }
}

/// <summary>Handles <see cref="ClearChequeCommand"/>.</summary>
public sealed class ClearChequeCommandHandler
    : ICommandHandler<ClearChequeCommand, ChequeStateResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly IVoucherRepository _vouchers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="ClearChequeCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public ClearChequeCommandHandler(
        IChequeRepository cheques,
        IVoucherRepository vouchers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _vouchers = vouchers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeStateResponse>> Handle(
        ClearChequeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(found.Error);
        }

        Cheque cheque = found.Value;

        // The clearing voucher is checked rather than taken on trust. A cheque
        // pointing at a voucher in another firm's books, or at one that was never
        // posted, would be untraceable exactly when somebody came looking - which
        // is during a bank reconciliation that will not balance.
        Voucher? clearing = await _vouchers.FindAsync(
            VoucherId.From(request.ClearingVoucherId), cancellationToken);

        if (clearing is null || clearing.FirmId != cheque.FirmId)
        {
            return Result.Failure<ChequeStateResponse>(Error.NotFound(
                "Cheque.ClearingVoucherNotFound",
                "No such voucher in the selected firm."));
        }

        if (clearing.Status != VoucherStatus.Posted)
        {
            return Result.Failure<ChequeStateResponse>(Error.BusinessRule(
                "Cheque.ClearingVoucherNotPosted",
                $"Voucher '{clearing.Number}' is {clearing.Status} and is not in the books, " +
                $"so it cannot account for a cleared cheque."));
        }

        Result cleared = cheque.Clear(request.ClearedOn, clearing.Id);

        if (cleared.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(cleared.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ChequeContext.StateOf(cheque));
    }
}

/// <summary>Handles <see cref="BounceChequeCommand"/>.</summary>
/// <remarks>
/// <para>
/// The consequential transition. A receipt settled by a cheque that later bounces
/// has to be undone: the bills it closed are owed again, and in the meantime the
/// customer's outstanding understated what they really owed. Releasing them happens
/// here, in the same transaction as the bounce, rather than through the domain event
/// - an eventually-consistent release would leave a window in which the books say an
/// invoice is paid and the bank says it never was.
/// </para>
/// <para>
/// The ledger postings are a different matter and are still never raised here. Which
/// control account a bounced cheque comes back out of, and where the bank's charge for
/// it goes, are a firm's own choice of chart; inventing an answer would mean posting
/// into somebody's books on a guess. What the bounce accepts instead is the voucher an
/// operator has already written for it, exactly as clearing accepts the one that posts
/// the bank movement. Where none is given the response says plainly that a reversing
/// entry is owed, so a caller cannot mistake silence for completeness.
/// </para>
/// </remarks>
public sealed class BounceChequeCommandHandler
    : ICommandHandler<BounceChequeCommand, BouncedChequeResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly IBillRepository _bills;
    private readonly IVoucherRepository _vouchers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="BounceChequeCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public BounceChequeCommandHandler(
        IChequeRepository cheques,
        IBillRepository bills,
        IVoucherRepository vouchers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _bills = bills;
        _vouchers = vouchers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<BouncedChequeResponse>> Handle(
        BounceChequeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<BouncedChequeResponse>(found.Error);
        }

        Cheque cheque = found.Value;
        VoucherId? reversal = null;

        // Checked before the bounce is recorded, so a cheque naming a voucher that
        // cannot stand as its reversal is refused whole rather than bounced with a
        // dangling reference to something that was never posted.
        if (request.ReversalVoucherId is { } named)
        {
            Result<Voucher> resolved = await ChequeContext.ResolveReversalAsync(
                _vouchers, cheque, named, cancellationToken);

            if (resolved.IsFailure)
            {
                return Result.Failure<BouncedChequeResponse>(resolved.Error);
            }

            reversal = resolved.Value.Id;
        }

        Result bounced = cheque.Bounce(request.Reason, request.BouncedOn, reversal);

        if (bounced.IsFailure)
        {
            return Result.Failure<BouncedChequeResponse>(bounced.Error);
        }

        IReadOnlyList<ReopenedBill> reopened = await ReleaseSettlementsAsync(
            cheque, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        decimal total = 0m;

        foreach (ReopenedBill bill in reopened)
        {
            total += bill.AmountReleased;
        }

        return Result.Success(new BouncedChequeResponse(
            ChequeContext.StateOf(cheque),
            reopened,
            total,
            LedgerReversalRequired: cheque.ReversalVoucherId is null));
    }

    /// <summary>Puts back the bills the bounced cheque's receipt had settled.</summary>
    /// <param name="cheque">The bounced cheque.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was released, bill by bill.</returns>
    /// <remarks>
    /// Released by the receipt that recorded the cheque, which is the only link the
    /// two have. A receipt made partly by cheque and partly in cash releases all of
    /// it, and that is the honest outcome: the allocations do not record which
    /// instrument paid which bill, so releasing a share of them would be a guess
    /// presented as a fact. The response says what was released so the operator can
    /// reallocate the part that stood.
    /// </remarks>
    private async Task<IReadOnlyList<ReopenedBill>> ReleaseSettlementsAsync(
        Cheque cheque,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Bill> settled = await _bills.FindAllocatedByAsync(
            cheque.OriginVoucherId, cancellationToken);

        List<ReopenedBill> reopened = [];

        foreach (Bill bill in settled)
        {
            Money released = bill.ReleaseAllocationsFrom(cheque.OriginVoucherId);

            if (released.IsZero)
            {
                continue;
            }

            reopened.Add(new ReopenedBill(
                bill.Id.Value, bill.BillNumber, released.Amount, bill.OutstandingAmount.Amount));
        }

        return reopened;
    }
}

/// <summary>Handles <see cref="RecordChequeReversalCommand"/>.</summary>
/// <remarks>
/// Closes the loop the bounce leaves open. Until this runs, the cheque's response says
/// a reversing entry is owed and the register shows it as unreversed; afterwards the
/// dishonour can be traced from the register to the entry that took it back out, which
/// is what anybody reconciling a bank statement is actually looking for.
/// </remarks>
public sealed class RecordChequeReversalCommandHandler
    : ICommandHandler<RecordChequeReversalCommand, ChequeStateResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly IVoucherRepository _vouchers;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="RecordChequeReversalCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public RecordChequeReversalCommandHandler(
        IChequeRepository cheques,
        IVoucherRepository vouchers,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _vouchers = vouchers;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeStateResponse>> Handle(
        RecordChequeReversalCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(found.Error);
        }

        Cheque cheque = found.Value;

        Result<Voucher> resolved = await ChequeContext.ResolveReversalAsync(
            _vouchers, cheque, request.ReversalVoucherId, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(resolved.Error);
        }

        Result recorded = cheque.RecordReversal(resolved.Value.Id);

        if (recorded.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(recorded.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ChequeContext.StateOf(cheque));
    }
}

/// <summary>Handles <see cref="StopChequeCommand"/>.</summary>
public sealed class StopChequeCommandHandler
    : ICommandHandler<StopChequeCommand, ChequeStateResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="StopChequeCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public StopChequeCommandHandler(
        IChequeRepository cheques,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeStateResponse>> Handle(
        StopChequeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(found.Error);
        }

        Cheque cheque = found.Value;
        Result stopped = cheque.Stop(request.Reason, request.StoppedOn);

        if (stopped.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(stopped.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ChequeContext.StateOf(cheque));
    }
}

/// <summary>Handles <see cref="CancelChequeCommand"/>.</summary>
public sealed class CancelChequeCommandHandler
    : ICommandHandler<CancelChequeCommand, ChequeStateResponse>
{
    private readonly IChequeRepository _cheques;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CancelChequeCommandHandler"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CancelChequeCommandHandler(
        IChequeRepository cheques,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cheques = cheques;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<ChequeStateResponse>> Handle(
        CancelChequeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Cheque> found = await ChequeContext.ResolveAsync(
            _cheques, _tenantContext, request.ChequeId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(found.Error);
        }

        Cheque cheque = found.Value;
        Result cancelled = cheque.Cancel(request.Reason, request.CancelledOn);

        if (cancelled.IsFailure)
        {
            return Result.Failure<ChequeStateResponse>(cancelled.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ChequeContext.StateOf(cheque));
    }
}
