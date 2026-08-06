using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>Whether a bill reference raises a bill or settles one already open.</summary>
/// <remarks>
/// The two halves of bill-wise settlement. An invoice raises a reference; the
/// receipt that pays it allocates against that reference. The operator states which
/// they mean rather than the system guessing, because both are legitimate on either
/// side of a party account: crediting a customer usually settles an invoice, but an
/// advance received is a genuine new obligation the other way round.
/// </remarks>
public enum BillReferenceKind
{
    /// <summary>Raises a new bill for the referenced amount.</summary>
    New = 1,

    /// <summary>Allocates against a bill that is already outstanding.</summary>
    Against = 2,
}

/// <summary>One bill reference attached to a voucher line.</summary>
/// <param name="Kind">Whether this raises a bill or settles one.</param>
/// <param name="Amount">
/// The amount referenced, in the voucher's entry currency. The references on a line
/// must account for the whole of it.
/// </param>
/// <param name="BillNumber">
/// The reference the party knows the bill by - usually an invoice number. Required
/// for <see cref="BillReferenceKind.New"/>, ignored otherwise.
/// </param>
/// <param name="BillId">
/// The bill being settled. Required for <see cref="BillReferenceKind.Against"/>,
/// ignored otherwise.
/// </param>
/// <param name="CreditDays">
/// Overrides the party's credit terms for this bill. Omitted, the ledger's credit
/// period applies, and a party with none set is due immediately.
/// </param>
public sealed record CreateVoucherBillReference(
    BillReferenceKind Kind,
    decimal Amount,
    string? BillNumber = null,
    Guid? BillId = null,
    int? CreditDays = null);

/// <summary>
/// Applies a voucher's bill references: raises the bills it creates and allocates
/// against the bills it settles.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="CreateVoucherCommandHandler"/> because it is the one
/// part of posting that touches a second aggregate. Keeping it here means the
/// handler still reads as "validate, number, build, post, save", and the rules that
/// decide which bills a posting affects sit in one place rather than threaded
/// through it.
/// </para>
/// <para>
/// Everything it does happens inside the command's transaction, so a voucher and
/// the settlements it makes commit together or not at all. A receipt that posted
/// while its allocations failed would leave the party's balance right and its
/// outstanding report wrong - the hardest kind of discrepancy to find, because
/// neither figure looks obviously broken on its own.
/// </para>
/// </remarks>
internal sealed class VoucherBillReferencePoster
{
    private readonly IBillRepository _bills;

    /// <summary>Initialises a new instance of the <see cref="VoucherBillReferencePoster"/> class.</summary>
    /// <param name="bills">The bill repository.</param>
    internal VoucherBillReferencePoster(IBillRepository bills) => _bills = bills;

    /// <summary>
    /// Raises and settles the bills named by a voucher's lines.
    /// </summary>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="lines">The command's lines, carrying the references.</param>
    /// <param name="ledgers">The ledgers those lines post against, already loaded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the first reason a reference was refused.</returns>
    internal async Task<Result> ApplyAsync(
        Voucher voucher,
        IReadOnlyList<CreateVoucherLine> lines,
        IReadOnlyDictionary<LedgerId, Ledger> ledgers,
        CancellationToken cancellationToken)
    {
        List<ReferencedLine> referenced = [.. lines
            .Where(line => line.BillReferences is { Count: > 0 })
            .Select(line => new ReferencedLine(
                line, ledgers[LedgerId.From(line.LedgerId)], line.BillReferences!))];

        if (referenced.Count == 0)
        {
            return Result.Success();
        }

        // A draft is not in the books. Raising a bill from one would put an invoice
        // on the outstanding report that nobody has posted, and there is nowhere to
        // hold the references until somebody does - allocations live on the bill,
        // not on the voucher. Refusing says so; accepting and dropping them would
        // not.
        if (voucher.Status != VoucherStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "Bill.DraftCannotCarryReferences",
                "Bill references can only be supplied on a voucher that is posted on " +
                "creation. Post the draft and record the settlement then."));
        }

        foreach (ReferencedLine line in referenced)
        {
            Result shape = ValidateShape(line, voucher.Currency);

            if (shape.IsFailure)
            {
                return shape;
            }
        }

        Result raised = await RaiseNewBillsAsync(voucher, referenced, cancellationToken);

        return raised.IsFailure
            ? raised
            : await AllocateAgainstExistingBillsAsync(voucher, referenced, cancellationToken);
    }

    /// <summary>
    /// States the kind of obligation a posting to a party account creates.
    /// </summary>
    /// <param name="side">The side the party ledger is posted on.</param>
    /// <returns>Receivable for a debit, payable for a credit.</returns>
    /// <remarks>
    /// Straight from double entry rather than from the ledger's kind: debiting a
    /// party means they owe the firm, crediting one means the firm owes them. Taking
    /// it from the kind instead would make a customer credit note impossible to
    /// express, and the domain is explicit that a credit note is a bill of the
    /// opposite type rather than a negative one.
    /// </remarks>
    private static BillType ObligationRaisedBy(EntrySide side) =>
        side == EntrySide.Debit ? BillType.Receivable : BillType.Payable;

    /// <summary>Checks a line's references before any of them are acted on.</summary>
    /// <param name="line">The line and its references.</param>
    /// <param name="currency">The voucher's entry currency.</param>
    /// <returns>Success, or the reason the line was refused.</returns>
    private static Result ValidateShape(ReferencedLine line, CurrencyCode currency)
    {
        if (!line.Ledger.IsBillWise)
        {
            return Result.Failure(Error.Validation(
                "Bill.LedgerNotBillWise",
                $"Ledger '{line.Ledger.Name}' is not tracked bill-wise, so a posting " +
                $"against it cannot reference bills."));
        }

        // The references must account for the whole line. A partial breakdown leaves
        // a remainder belonging to no bill, which is exactly what makes an
        // outstanding report stop reconciling with the party's balance. A receipt
        // genuinely on account carries no references at all.
        decimal referenced = 0m;

        foreach (CreateVoucherBillReference reference in line.References)
        {
            referenced += reference.Amount;
        }

        if (referenced != line.Amount)
        {
            return Result.Failure(Error.Validation(
                "Bill.ReferencesDoNotMatchLine",
                $"The bill references on the line for '{line.Ledger.Name}' total " +
                $"{Money.Of(referenced, currency)}, but the line is for " +
                $"{Money.Of(line.Amount, currency)}. Reference the whole line, or none " +
                $"of it."));
        }

        foreach (CreateVoucherBillReference reference in line.References)
        {
            if (reference.Kind == BillReferenceKind.New && string.IsNullOrWhiteSpace(reference.BillNumber))
            {
                return Result.Failure(Error.Validation(
                    "Bill.NumberRequired", "A new bill reference needs a bill number."));
            }

            if (reference.Kind == BillReferenceKind.Against && reference.BillId is null)
            {
                return Result.Failure(Error.Validation(
                    "Bill.ReferenceRequired",
                    "A reference against an existing bill must name that bill."));
            }
        }

        return Result.Success();
    }

    /// <summary>Raises every bill the voucher creates.</summary>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="lines">The lines carrying references.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the reason a bill could not be raised.</returns>
    private async Task<Result> RaiseNewBillsAsync(
        Voucher voucher,
        IReadOnlyList<ReferencedLine> lines,
        CancellationToken cancellationToken)
    {
        foreach (ReferencedLine line in lines)
        {
            List<CreateVoucherBillReference> fresh = [.. line.References
                .Where(r => r.Kind == BillReferenceKind.New)];

            if (fresh.Count == 0)
            {
                continue;
            }

            Result unique = await EnsureReferencesAreUnusedAsync(line, fresh, cancellationToken);

            if (unique.IsFailure)
            {
                return unique;
            }

            foreach (CreateVoucherBillReference reference in fresh)
            {
                Result<Bill> bill = Bill.Raise(
                    voucher.TenantId,
                    voucher.FirmId,
                    line.Ledger.Id,
                    voucher.Id,
                    ObligationRaisedBy(line.Side),
                    reference.BillNumber!,
                    voucher.Date,
                    reference.CreditDays ?? line.Ledger.CreditDays ?? 0,
                    Money.Of(reference.Amount, voucher.Currency));

                if (bill.IsFailure)
                {
                    return Result.Failure(bill.Error);
                }

                _bills.Add(bill.Value);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Refuses a bill reference the party already has, whether it is in the database
    /// or elsewhere in this same voucher.
    /// </summary>
    /// <param name="line">The line raising the bills.</param>
    /// <param name="fresh">The new references on it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the clash.</returns>
    /// <remarks>
    /// A unique index enforces this anyway. Checking first turns a constraint
    /// violation surfacing as a 500 into a message naming the reference that clashed,
    /// which is the difference between an operator fixing their own typo and raising
    /// a support ticket. The within-voucher case matters just as much and no index
    /// catches it before the save.
    /// </remarks>
    private async Task<Result> EnsureReferencesAreUnusedAsync(
        ReferencedLine line,
        IReadOnlyList<CreateVoucherBillReference> fresh,
        CancellationToken cancellationToken)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (CreateVoucherBillReference reference in fresh)
        {
            string number = reference.BillNumber!.Trim();

            if (!seen.Add(number))
            {
                return Result.Failure(Error.Validation(
                    "Bill.DuplicateReferenceInVoucher",
                    $"This voucher raises bill '{number}' for '{line.Ledger.Name}' more " +
                    $"than once."));
            }
        }

        IReadOnlySet<string> taken = await _bills.FindExistingReferencesAsync(
            line.Ledger.FirmId, line.Ledger.Id, seen, cancellationToken);

        if (taken.Count == 0)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Conflict(
            "Bill.ReferenceAlreadyUsed",
            $"'{line.Ledger.Name}' already has a bill numbered " +
            $"{string.Join(", ", taken.Order(StringComparer.Ordinal).Select(n => $"'{n}'"))}."));
    }

    /// <summary>Allocates against every bill the voucher settles.</summary>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="lines">The lines carrying references.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the reason an allocation was refused.</returns>
    private async Task<Result> AllocateAgainstExistingBillsAsync(
        Voucher voucher,
        IReadOnlyList<ReferencedLine> lines,
        CancellationToken cancellationToken)
    {
        List<BillId> wanted = [.. lines
            .SelectMany(line => line.References)
            .Where(r => r.Kind == BillReferenceKind.Against)
            .Select(r => BillId.From(r.BillId!.Value))
            .Distinct()];

        if (wanted.Count == 0)
        {
            return Result.Success();
        }

        // One query for every bill the voucher touches. Loading them inside the loop
        // would be a round trip per reference, and a receipt clearing a month of
        // invoices has plenty of them.
        IReadOnlyDictionary<BillId, Bill> bills =
            await _bills.GetManyAsync(wanted, cancellationToken);

        foreach (ReferencedLine line in lines)
        {
            foreach (CreateVoucherBillReference reference in line.References)
            {
                if (reference.Kind != BillReferenceKind.Against)
                {
                    continue;
                }

                Result allocated = Allocate(voucher, line, reference, bills);

                if (allocated.IsFailure)
                {
                    return allocated;
                }
            }
        }

        return Result.Success();
    }

    /// <summary>Applies one allocation, having checked it belongs where it claims.</summary>
    /// <param name="voucher">The settling voucher.</param>
    /// <param name="line">The line making the settlement.</param>
    /// <param name="reference">The reference being applied.</param>
    /// <param name="bills">The bills loaded for this voucher.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    private static Result Allocate(
        Voucher voucher,
        ReferencedLine line,
        CreateVoucherBillReference reference,
        IReadOnlyDictionary<BillId, Bill> bills)
    {
        BillId billId = BillId.From(reference.BillId!.Value);

        if (!bills.TryGetValue(billId, out Bill? bill))
        {
            return Result.Failure(Error.NotFound(
                "Bill.NotFound", $"Bill {billId} does not exist."));
        }

        // The tenant filter already prevents reading another tenant's bill. These
        // catch the two subtler cases: a bill belonging to a sibling firm, and one
        // belonging to a different party. Either would settle a debt the payer does
        // not owe and leave two outstanding balances wrong at once.
        if (bill.FirmId != voucher.FirmId)
        {
            return Result.Failure(Error.Validation(
                "Bill.WrongFirm", $"Bill '{bill.BillNumber}' belongs to a different firm."));
        }

        if (bill.LedgerId != line.Ledger.Id)
        {
            return Result.Failure(Error.Validation(
                "Bill.WrongParty",
                $"Bill '{bill.BillNumber}' does not belong to '{line.Ledger.Name}'."));
        }

        // Settling a receivable credits the party and settling a payable debits one.
        // A posting on the same side as the bill increases the obligation rather
        // than discharging it, and belongs on a new reference.
        if (bill.Type == ObligationRaisedBy(line.Side))
        {
            return Result.Failure(Error.Validation(
                "Bill.WrongSideForSettlement",
                $"Bill '{bill.BillNumber}' is a {bill.Type.ToString().ToLowerInvariant()} " +
                $"and cannot be settled by a {line.Side.ToString().ToLowerInvariant()} to " +
                $"'{line.Ledger.Name}'. Raise a new reference instead."));
        }

        return bill.Allocate(
            voucher.Id, Money.Of(reference.Amount, voucher.Currency), voucher.Date);
    }

    /// <summary>A command line paired with the ledger and references it carries.</summary>
    /// <param name="Line">The command line.</param>
    /// <param name="Ledger">The ledger it posts against.</param>
    /// <param name="References">The bill references on it.</param>
    private sealed record ReferencedLine(
        CreateVoucherLine Line,
        Ledger Ledger,
        IReadOnlyList<CreateVoucherBillReference> References)
    {
        /// <summary>Gets the side the party ledger is posted on.</summary>
        internal EntrySide Side => Line.Side;

        /// <summary>Gets the line amount, in the entry currency.</summary>
        internal decimal Amount => Line.Amount;
    }
}
