using ERP.Application.Abstractions.Persistence;
using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Accounting.Cheques;

/// <summary>Writes the journal a dishonoured cheque owes the ledger.</summary>
/// <remarks>
/// <para>
/// The business's answer of 2026-08-10, finally built. A bounce used to leave the books
/// alone and return <c>ledgerReversalRequired</c>, waiting for somebody to write the
/// journal by hand - which was honest while nobody had said which accounts to use, and
/// became merely unfinished once they had.
/// </para>
/// <para>
/// A cheque can only bounce from <b>deposited</b>, so the money never reached the bank:
/// it is sitting in cheques in hand, where the receipt put it. The reversal is therefore
/// the receipt backwards - the party owes again, and cheques in hand gives the money up -
/// and nothing is left in suspense afterwards. That is why the dishonour suspense account
/// the business also named is not among the ones this posts to: under this model there is
/// no moment where the money is in neither place, and an account that only ever holds
/// zero is one more thing on a trial balance for a reader to wonder about.
/// </para>
/// <para>
/// The operator's own route is untouched. A bounce that names a voucher is honoured as it
/// always was, and this raises nothing - somebody who has written the entry themselves
/// does not want a second one appearing beside it.
/// </para>
/// </remarks>
internal sealed class ChequeReversalPoster
{
    private readonly IVoucherRepository _vouchers;
    private readonly INumberingSeriesRepository _numbering;
    private readonly IFinancialYearRepository _financialYears;

    internal ChequeReversalPoster(
        IVoucherRepository vouchers,
        INumberingSeriesRepository numbering,
        IFinancialYearRepository financialYears)
    {
        _vouchers = vouchers;
        _numbering = numbering;
        _financialYears = financialYears;
    }

    /// <summary>Checks the accounts a bounce will need, before anything is changed.</summary>
    /// <param name="map">The firm's account map, or nothing where it has none.</param>
    /// <param name="bankCharge">The charge the caller stated, if any.</param>
    /// <returns>Success, or the reason the bounce cannot be posted.</returns>
    /// <remarks>
    /// Separate from raising it so the cheque is not marked bounced by a call that is
    /// about to refuse. The transaction would roll that back, but an aggregate left in a
    /// state nothing asked for is a trap for whatever reads it next.
    /// </remarks>
    internal static Result EnsureReady(InventoryAccountMap? map, decimal bankCharge)
    {
        if (map is null)
        {
            return Result.Failure(Error.BusinessRule(
                "InventoryAccounts.NotConfigured",
                "This firm has not chosen which accounts a bounced cheque posts to."));
        }

        Result<LedgerId> chequesInHand = map.For(StockAccount.ChequesInHand);

        if (chequesInHand.IsFailure)
        {
            return Result.Failure(chequesInHand.Error);
        }

        return bankCharge > 0m
            ? map.For(StockAccount.BankCharges)
            : Result.Success();
    }

    /// <summary>Raises and posts the journal for a cheque that has just bounced.</summary>
    /// <param name="cheque">The cheque, already bounced.</param>
    /// <param name="firm">The firm, for its base currency.</param>
    /// <param name="map">The firm's account map, already checked.</param>
    /// <param name="branchId">The branch the journal belongs to.</param>
    /// <param name="bankCharge">What the bank charged for the dishonour, if anything.</param>
    /// <param name="postedBy">The user recording the bounce.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The journal raised, or the reason the bounce cannot be recorded.</returns>
    internal async Task<Result<Voucher>> RaiseAsync(
        Cheque cheque,
        Firm firm,
        InventoryAccountMap map,
        BranchId branchId,
        decimal bankCharge,
        UserId postedBy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cheque);
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(map);

        Result<LedgerId> chequesInHand = map.For(StockAccount.ChequesInHand);

        if (chequesInHand.IsFailure)
        {
            return Result.Failure<Voucher>(chequesInHand.Error);
        }

        FinancialYear? year = await _financialYears.FindContainingAsync(
            cheque.FirmId, cheque.ClosedOn ?? cheque.InstrumentDate, cancellationToken);

        if (year is null)
        {
            return Result.Failure<Voucher>(Error.BusinessRule(
                "FinancialYear.NotFoundForDate",
                $"No financial year covers {cheque.ClosedOn:yyyy-MM-dd}."));
        }

        Result<string> number = await JournalNumbering.ReserveAsync(
            _numbering, cheque.TenantId, cheque.FirmId, branchId, year, cancellationToken);

        if (number.IsFailure)
        {
            return Result.Failure<Voucher>(number.Error);
        }

        Result<Voucher> draft = Voucher.CreateDraft(
            cheque.TenantId,
            cheque.FirmId,
            branchId,
            year,
            VoucherType.Journal,
            number.Value,
            cheque.ClosedOn ?? cheque.InstrumentDate,
            firm.BaseCurrency,
            firm.BaseCurrency);

        if (draft.IsFailure)
        {
            return draft;
        }

        Voucher journal = draft.Value;
        string narration = $"Cheque {cheque.ChequeNumber} dishonoured";

        // A cheque taken in is a debt that was never really settled, so the party owes
        // again and cheques in hand gives the money up. One the firm wrote runs the
        // other way: the supplier is owed again and the money never left.
        bool received = cheque.Direction == ChequeDirection.Received;

        Result posted = AddPair(
            journal,
            debit: received ? cheque.PartyLedgerId : chequesInHand.Value,
            credit: received ? chequesInHand.Value : cheque.PartyLedgerId,
            amount: cheque.Amount.Amount,
            narration);

        if (posted.IsFailure)
        {
            return Result.Failure<Voucher>(posted.Error);
        }

        Result charged = await AddBankChargeAsync(
            journal, cheque, map, bankCharge, narration);

        if (charged.IsFailure)
        {
            return Result.Failure<Voucher>(charged.Error);
        }

        Result final = journal.Post(postedBy, nowUtc);

        if (final.IsFailure)
        {
            return Result.Failure<Voucher>(final.Error);
        }

        _vouchers.Add(journal);

        return Result.Success(journal);
    }

    /// <summary>Adds the bank's charge, where the operator stated one.</summary>
    /// <remarks>
    /// Charged to the bank the cheque was deposited to, which the cheque already knows -
    /// so nobody is asked which account the fee came out of when they can see it on the
    /// advice in front of them. A cheque with no bank recorded is refused rather than
    /// having its fee posted somewhere invented.
    /// </remarks>
    private static Task<Result> AddBankChargeAsync(
        Voucher journal,
        Cheque cheque,
        InventoryAccountMap map,
        decimal bankCharge,
        string narration)
    {
        if (bankCharge <= 0m)
        {
            return Task.FromResult(Result.Success());
        }

        Result<LedgerId> charges = map.For(StockAccount.BankCharges);

        if (charges.IsFailure)
        {
            return Task.FromResult(Result.Failure(charges.Error));
        }

        if (cheque.BankLedgerId is not { } bank)
        {
            return Task.FromResult(Result.Failure(Error.BusinessRule(
                "Cheque.NoBankForCharge",
                $"Cheque '{cheque.ChequeNumber}' names no bank account, so the charge has "
                + "nowhere to come out of.")));
        }

        return Task.FromResult(AddPair(
            journal, debit: charges.Value, credit: bank, amount: bankCharge, narration));
    }

    /// <summary>Adds both sides of one movement.</summary>
    private static Result AddPair(
        Voucher journal,
        LedgerId debit,
        LedgerId credit,
        decimal amount,
        string narration)
    {
        decimal rounded = decimal.Round(
            amount, journal.Currency.DecimalPlaces, MidpointRounding.AwayFromZero);

        if (rounded <= 0m)
        {
            return Result.Success();
        }

        Result<VoucherLine> debited = journal.AddLine(
            debit, EntrySide.Debit, rounded, narration);

        if (debited.IsFailure)
        {
            return Result.Failure(debited.Error);
        }

        Result<VoucherLine> credited = journal.AddLine(
            credit, EntrySide.Credit, rounded, narration);

        return credited.IsFailure ? Result.Failure(credited.Error) : Result.Success();
    }
}
