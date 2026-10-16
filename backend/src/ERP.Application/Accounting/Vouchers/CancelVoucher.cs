using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using FluentValidation;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>Cancels a receipt, payment, journal or contra, taking it out of the books.</summary>
/// <param name="VoucherId">The voucher.</param>
/// <param name="Reason">Why. Required, and kept on the voucher.</param>
/// <remarks>
/// The voucher keeps its number and its lines; what it did is undone. The bills it settled
/// are owed again, the bills it raised are withdrawn, and the cheques it recorded are
/// cancelled while they are still in hand. A journal an invoice or a stock document wrote
/// is refused by name: it is that document's to undo.
/// </remarks>
public sealed record CancelVoucherCommand(Guid VoucherId, string Reason)
    : ICommand, ITransactional;

/// <summary>Takes a posted voucher back to a draft, so it can be corrected and posted again.</summary>
/// <param name="VoucherId">The voucher.</param>
/// <param name="Reason">Why, recorded against the cheques it withdraws.</param>
/// <remarks>
/// The first half of <see cref="UpdateVoucherCommand"/>, which sends it, rewrites the draft
/// and posts it again in the same transaction. Never sent on its own by the API.
/// </remarks>
public sealed record ReopenVoucherCommand(Guid VoucherId, string Reason)
    : ICommand, ITransactional;

/// <summary>Validates <see cref="CancelVoucherCommand"/>.</summary>
public sealed class CancelVoucherCommandValidator : AbstractValidator<CancelVoucherCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelVoucherCommandValidator"/> class.</summary>
    public CancelVoucherCommandValidator()
    {
        RuleFor(c => c.VoucherId).NotEqual(Guid.Empty);

        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("A reason is required when cancelling a voucher.")
            .MaximumLength(500);
    }
}

/// <summary>Handles <see cref="CancelVoucherCommand"/> and <see cref="ReopenVoucherCommand"/>.</summary>
/// <remarks>
/// One path for both: what has to be undone is the same, and they part only at the end,
/// where one voucher is cancelled and the other goes back to being a draft.
/// </remarks>
public sealed class CancelVoucherCommandHandler
    : ICommandHandler<CancelVoucherCommand>, ICommandHandler<ReopenVoucherCommand>
{
    private readonly IVoucherRepository _vouchers;
    private readonly IBillRepository _bills;
    private readonly IChequeRepository _cheques;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="CancelVoucherCommandHandler"/> class.</summary>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="bills">The bill repository.</param>
    /// <param name="cheques">The cheque repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public CancelVoucherCommandHandler(
        IVoucherRepository vouchers,
        IBillRepository bills,
        IChequeRepository cheques,
        ITenantContext tenantContext,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _vouchers = vouchers;
        _bills = bills;
        _cheques = cheques;
        _tenantContext = tenantContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<Result> Handle(CancelVoucherCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return UndoAsync(request.VoucherId, request.Reason, reopen: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> Handle(ReopenVoucherCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return UndoAsync(request.VoucherId, request.Reason, reopen: true, cancellationToken);
    }

    /// <summary>Checks a voucher may be changed by hand at all.</summary>
    /// <param name="vouchers">The voucher repository.</param>
    /// <param name="voucher">The voucher.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Success, or the document whose voucher this is.</returns>
    internal static async Task<Result> EnsureEnteredByHandAsync(
        IVoucherRepository vouchers,
        Voucher voucher,
        CancellationToken cancellationToken)
    {
        string? raisedBy = await vouchers.FindRaisingDocumentAsync(voucher.Id, cancellationToken);

        return raisedBy is null
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "Voucher.RaisedByDocument",
                $"'{voucher.Number}' was written by {raisedBy}. Edit or cancel {raisedBy} "
                + "instead, and its entry in the books follows."));
    }

    private async Task<Result> UndoAsync(
        Guid voucherId,
        string reason,
        bool reopen,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "Voucher.NoFirmSelected", "A firm must be selected to cancel a voucher."));
        }

        Voucher? voucher = await _vouchers.FindAsync(
            VoucherId.From(voucherId), cancellationToken);

        if (voucher is null || voucher.FirmId != firmId)
        {
            return Result.Failure(Error.NotFound(
                "Voucher.NotFound", "That voucher does not exist in the selected firm."));
        }

        if (voucher.Status == VoucherStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.AlreadyCancelled", $"Voucher '{voucher.Number}' is already cancelled."));
        }

        if (reopen && voucher.Status != VoucherStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "Voucher.NotPosted",
                $"Only a posted voucher can be reopened, and '{voucher.Number}' is {voucher.Status}."));
        }

        Result manual = await EnsureEnteredByHandAsync(_vouchers, voucher, cancellationToken);

        if (manual.IsFailure)
        {
            return manual;
        }

        // Every refusal before anything is touched.
        IReadOnlyList<Bill> raised = await _bills.FindRaisedByAsync(voucher.Id, cancellationToken);

        foreach (Bill bill in raised.Where(bill => bill.Status != BillStatus.Cancelled))
        {
            if (!bill.SettledAmount.IsZero)
            {
                return Result.Failure(Error.BusinessRule(
                    "Bill.PartlySettled",
                    $"Bill '{bill.BillNumber}', raised by '{voucher.Number}', has "
                    + $"{bill.SettledAmount} settled against it. Cancel the vouchers that "
                    + "settled it first."));
            }
        }

        IReadOnlyList<Cheque> cheques =
            await _cheques.FindRecordedByAsync(voucher.Id, cancellationToken);

        foreach (Cheque cheque in cheques)
        {
            if (cheque.Status is ChequeStatus.Deposited or ChequeStatus.Cleared)
            {
                return Result.Failure(Error.BusinessRule(
                    "Cheque.AlreadyPresented",
                    $"Cheque '{cheque.ChequeNumber}' on '{voucher.Number}' has already gone "
                    + "to the bank. Bounce or reverse it from the cheque register instead."));
            }
        }

        IReadOnlyList<Bill> settled = await _bills.FindAllocatedByAsync(voucher.Id, cancellationToken);

        foreach (Bill bill in settled)
        {
            bill.ReleaseAllocationsFrom(voucher.Id);
        }

        foreach (Bill bill in raised.Where(bill => bill.Status != BillStatus.Cancelled))
        {
            Result withdrawn = bill.Cancel();

            if (withdrawn.IsFailure)
            {
                return withdrawn;
            }
        }

        string note = reopen ? $"Voucher {voucher.Number} edited: {reason}" : reason;
        DateOnly today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        foreach (Cheque cheque in cheques.Where(cheque => cheque.Status == ChequeStatus.Pending))
        {
            Result withdrawn = cheque.Cancel(note, today);

            if (withdrawn.IsFailure)
            {
                return withdrawn;
            }
        }

        Result changed = reopen ? voucher.Reopen() : voucher.Cancel(reason);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
