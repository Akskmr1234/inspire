using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>One cheque changing hands as part of a voucher line.</summary>
/// <param name="ChequeNumber">The number printed on the cheque.</param>
/// <param name="InstrumentDate">
/// The date written on its face. Later than the voucher's date makes it post-dated,
/// which is not a separate kind of record - only a cheque that cannot be banked yet.
/// </param>
/// <param name="Amount">
/// The amount, in the voucher's entry currency. The cheques on a line must account
/// for the whole of it.
/// </param>
/// <param name="BankLedgerId">
/// The firm's account. Required when the firm is issuing the cheque, since it is
/// drawn on a known account; omitted when receiving one, whose account is chosen
/// when it is banked.
/// </param>
/// <param name="DrawnOnBank">The bank named on a received cheque, if known.</param>
public sealed record CreateVoucherCheque(
    string ChequeNumber,
    DateOnly InstrumentDate,
    decimal Amount,
    Guid? BankLedgerId = null,
    string? DrawnOnBank = null);

/// <summary>Records the cheques a voucher's lines bring into the register.</summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="VoucherBillReferencePoster"/>, and for the same
/// reason: a receipt and the instruments it was made with are one act, and a
/// register that could diverge from the postings behind it would be worse than no
/// register at all.
/// </para>
/// <para>
/// Only the recording happens here. What a cheque does next - being banked,
/// clearing, bouncing - is a separate event days or weeks later, with its own
/// command.
/// </para>
/// </remarks>
internal sealed class VoucherChequeRecorder
{
    private readonly IChequeRepository _cheques;

    /// <summary>Initialises a new instance of the <see cref="VoucherChequeRecorder"/> class.</summary>
    /// <param name="cheques">The cheque repository.</param>
    internal VoucherChequeRecorder(IChequeRepository cheques) => _cheques = cheques;

    /// <summary>Records every cheque named by a voucher's lines.</summary>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="lines">The command's lines, carrying the cheques.</param>
    /// <param name="ledgers">The ledgers those lines post against, already loaded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the first reason a cheque was refused.</returns>
    internal async Task<Result> RecordAsync(
        Voucher voucher,
        IReadOnlyList<CreateVoucherLine> lines,
        IReadOnlyDictionary<LedgerId, Ledger> ledgers,
        CancellationToken cancellationToken)
    {
        List<ChequeLine> carrying = [.. lines
            .Where(line => line.Cheques is { Count: > 0 })
            .Select(line => new ChequeLine(
                line, ledgers[LedgerId.From(line.LedgerId)], line.Cheques!))];

        if (carrying.Count == 0)
        {
            return Result.Success();
        }

        // A draft is not in the books, and a cheque in the register that no posting
        // accounts for would appear on the PDC report as money expected against a
        // receipt nobody made.
        if (voucher.Status != VoucherStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "Cheque.DraftCannotCarryCheques",
                "Cheques can only be recorded on a voucher that is posted on creation."));
        }

        foreach (ChequeLine line in carrying)
        {
            Result shape = ValidateShape(line, voucher.Currency);

            if (shape.IsFailure)
            {
                return shape;
            }

            Result unused = await EnsureNumbersAreUnusedAsync(line, cancellationToken);

            if (unused.IsFailure)
            {
                return unused;
            }

            Result recorded = Record(voucher, line, ledgers);

            if (recorded.IsFailure)
            {
                return recorded;
            }
        }

        return Result.Success();
    }

    /// <summary>States which way a cheque runs, from the side its party is posted on.</summary>
    /// <param name="side">The side the party ledger is posted on.</param>
    /// <returns>Received for a credit, issued for a debit.</returns>
    /// <remarks>
    /// Crediting a party discharges what they owe, which is what taking a cheque in
    /// does; debiting one discharges what the firm owes, which is what writing a
    /// cheque out does. Straight from double entry, exactly as the direction of a
    /// bill is - and it means an operator cannot record a receipt that files itself
    /// under payments.
    /// </remarks>
    private static ChequeDirection DirectionFor(EntrySide side) =>
        side == EntrySide.Credit ? ChequeDirection.Received : ChequeDirection.Issued;

    /// <summary>Checks a line's cheques before any of them are recorded.</summary>
    /// <param name="line">The line and its cheques.</param>
    /// <param name="currency">The voucher's entry currency.</param>
    /// <returns>Success, or the reason the line was refused.</returns>
    private static Result ValidateShape(ChequeLine line, CurrencyCode currency)
    {
        // Cash and bank ledgers are the firm's own money and cannot be the other
        // party to a cheque. Without this, cheques attached to the bank line of a
        // receipt would take their direction from that line and file every incoming
        // cheque as one the firm had issued.
        if (line.Ledger.Kind is LedgerKind.Cash or LedgerKind.Bank)
        {
            return Result.Failure(Error.Validation(
                "Cheque.NotAPartyLedger",
                $"'{line.Ledger.Name}' is one of the firm's own accounts. Record the cheque " +
                $"against the party it came from or goes to."));
        }

        decimal covered = 0m;

        foreach (CreateVoucherCheque cheque in line.Cheques)
        {
            covered += cheque.Amount;
        }

        // The same rule bill references follow. A line part-covered by cheques
        // leaves a remainder settled by nothing in particular, and the register
        // stops reconciling with the postings behind it.
        if (covered != line.Amount)
        {
            return Result.Failure(Error.Validation(
                "Cheque.AmountsDoNotMatchLine",
                $"The cheques on the line for '{line.Ledger.Name}' total " +
                $"{Money.Of(covered, currency)}, but the line is for " +
                $"{Money.Of(line.Amount, currency)}. Record the whole line in cheques, or " +
                $"none of it."));
        }

        return Result.Success();
    }

    /// <summary>Refuses a cheque number the party already has live.</summary>
    /// <param name="line">The line recording the cheques.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Success, or the clash.</returns>
    /// <remarks>
    /// A filtered unique index enforces this too. Checking first names the number
    /// that clashed rather than surfacing a constraint violation as a 500, and it
    /// catches the same number appearing twice within one voucher, which no index
    /// can do before the insert.
    /// </remarks>
    private async Task<Result> EnsureNumbersAreUnusedAsync(
        ChequeLine line,
        CancellationToken cancellationToken)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (CreateVoucherCheque cheque in line.Cheques)
        {
            string number = cheque.ChequeNumber?.Trim() ?? string.Empty;

            if (!seen.Add(number))
            {
                return Result.Failure(Error.Validation(
                    "Cheque.DuplicateNumberInVoucher",
                    $"This voucher records cheque '{number}' for '{line.Ledger.Name}' more " +
                    $"than once."));
            }
        }

        IReadOnlySet<string> live = await _cheques.FindLiveNumbersAsync(
            line.Ledger.FirmId, line.Ledger.Id, seen, cancellationToken);

        if (live.Count == 0)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Conflict(
            "Cheque.NumberAlreadyLive",
            $"'{line.Ledger.Name}' already has an open cheque numbered " +
            $"{string.Join(", ", live.Order(StringComparer.Ordinal).Select(n => $"'{n}'"))}."));
    }

    /// <summary>Builds and adds a line's cheques.</summary>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="line">The line and its cheques.</param>
    /// <param name="ledgers">The ledgers loaded for this voucher.</param>
    /// <returns>Success, or the reason one was refused.</returns>
    private Result Record(
        Voucher voucher,
        ChequeLine line,
        IReadOnlyDictionary<LedgerId, Ledger> ledgers)
    {
        ChequeDirection direction = DirectionFor(line.Side);

        foreach (CreateVoucherCheque details in line.Cheques)
        {
            Result<LedgerId?> bank = ResolveBankAccount(details, voucher, ledgers);

            if (bank.IsFailure)
            {
                return Result.Failure(bank.Error);
            }

            Result<Cheque> cheque = Cheque.Record(
                voucher.TenantId,
                voucher.FirmId,
                direction,
                line.Ledger.Id,
                voucher.Id,
                details.ChequeNumber,
                details.InstrumentDate,
                voucher.Date,
                Money.Of(details.Amount, voucher.Currency),
                bank.Value,
                details.DrawnOnBank);

            if (cheque.IsFailure)
            {
                return Result.Failure(cheque.Error);
            }

            _cheques.Add(cheque.Value);
        }

        return Result.Success();
    }

    /// <summary>Resolves and checks the firm's account a cheque is drawn on.</summary>
    /// <param name="details">The cheque details supplied.</param>
    /// <param name="voucher">The voucher being posted.</param>
    /// <param name="ledgers">The ledgers loaded for this voucher.</param>
    /// <returns>The account, or the reason it was refused.</returns>
    /// <remarks>
    /// Whether one is required at all is the domain's rule, not this method's - an
    /// issued cheque needs one and a received cheque does not. What is checked here
    /// is that a named account exists, belongs to this firm, and is actually a bank
    /// account. A cheque drawn on the sales ledger would pass every other check and
    /// make the bank reconciliation nonsense.
    /// </remarks>
    private static Result<LedgerId?> ResolveBankAccount(
        CreateVoucherCheque details,
        Voucher voucher,
        IReadOnlyDictionary<LedgerId, Ledger> ledgers)
    {
        if (details.BankLedgerId is not { } requested)
        {
            return Result.Success<LedgerId?>(null);
        }

        LedgerId bankId = LedgerId.From(requested);

        if (!ledgers.TryGetValue(bankId, out Ledger? bank))
        {
            return Result.Failure<LedgerId?>(Error.NotFound(
                "Cheque.BankAccountNotFound",
                "The account a cheque is drawn on must be one of the voucher's own lines."));
        }

        if (bank.FirmId != voucher.FirmId)
        {
            return Result.Failure<LedgerId?>(Error.Validation(
                "Cheque.BankAccountWrongFirm",
                $"Account '{bank.Name}' belongs to a different firm."));
        }

        return bank.Kind != LedgerKind.Bank
            ? Result.Failure<LedgerId?>(Error.Validation(
                "Cheque.NotABankAccount",
                $"'{bank.Name}' is not a bank account, so a cheque cannot be drawn on it."))
            : Result.Success<LedgerId?>(bankId);
    }

    /// <summary>A command line paired with the ledger and cheques it carries.</summary>
    /// <param name="Line">The command line.</param>
    /// <param name="Ledger">The ledger it posts against.</param>
    /// <param name="Cheques">The cheques on it.</param>
    private sealed record ChequeLine(
        CreateVoucherLine Line,
        Ledger Ledger,
        IReadOnlyList<CreateVoucherCheque> Cheques)
    {
        /// <summary>Gets the side the party ledger is posted on.</summary>
        internal EntrySide Side => Line.Side;

        /// <summary>Gets the line amount, in the entry currency.</summary>
        internal decimal Amount => Line.Amount;
    }
}
