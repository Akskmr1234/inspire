using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Inventory.Stock;

/// <summary>Turns what a stock document moved into what it did to the accounts.</summary>
/// <remarks>
/// <para>
/// The answer to open question 8a, made arithmetic. Inventory is debited when goods
/// come in and credited when they go out; the other side comes from the firm's own map,
/// because which account a movement lands in is a reading of their chart and not
/// something this system may decide for them.
/// </para>
/// <para>
/// Built from the stock ledger entries rather than from the document's lines. The lines
/// say what somebody entered; the entries say what the posting actually did, including
/// the cost an issue or a transfer was valued at - which was never on a line. A journal
/// built from the lines would be right until the first issue, and wrong quietly.
/// </para>
/// </remarks>
internal static class StockJournal
{
    /// <summary>The counter-account each kind of document posts against.</summary>
    /// <param name="type">The kind of stock document.</param>
    /// <param name="incoming">Whether the movement brought goods in.</param>
    /// <returns>The account, or null where the document posts nothing at all.</returns>
    /// <remarks>
    /// A material receipt credits consumption rather than a variance, which is the
    /// business's answer of 2026-08-10: a receipt is the mirror of an issue - goods
    /// coming back from a department or from production - so the two net off over a job
    /// instead of leaving a permanent balance in a correction account.
    /// <para>
    /// A count is split by direction rather than given one account. Goods found are a
    /// correction; goods missing are a loss, and reporting the two in one account would
    /// hide the second inside the first.
    /// </para>
    /// </remarks>
    internal static StockAccount? CounterAccountFor(StockDocumentType type, bool incoming) =>
        type switch
        {
            // A transfer changes which shelf goods are on. It changes neither whose
            // they are nor what they are worth, so there is nothing for the accounts
            // to say about it.
            StockDocumentType.StockTransfer => null,

            StockDocumentType.OpeningStock => StockAccount.OpeningEquity,
            StockDocumentType.MaterialReceipt or StockDocumentType.MaterialIssue =>
                StockAccount.Consumption,
            StockDocumentType.DamagedStock => StockAccount.Loss,
            StockDocumentType.StockAdjustment => StockAccount.Variance,
            StockDocumentType.PhysicalVerification =>
                incoming ? StockAccount.Variance : StockAccount.Loss,
            _ => StockAccount.Variance,
        };

    /// <summary>Raises the journal a posted stock document owes the nominal ledger.</summary>
    /// <param name="document">The document, already posted.</param>
    /// <param name="movements">The stock ledger entries it wrote.</param>
    /// <param name="map">The firm's account map.</param>
    /// <param name="firm">The firm, for its base currency.</param>
    /// <param name="branchId">The branch the journal belongs to.</param>
    /// <param name="year">The financial year it falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <returns>The journal, null where the document owes none, or the first refusal.</returns>
    /// <remarks>
    /// One journal for the whole document rather than one per line: a receipt of forty
    /// products is one event in the books, and forty journals would make a day book
    /// unreadable to reconcile a single delivery note.
    /// </remarks>
    internal static Result<Voucher?> Raise(
        StockDocument document,
        IReadOnlyList<StockLedgerEntry> movements,
        InventoryAccountMap map,
        Firm firm,
        BranchId branchId,
        FinancialYear year,
        string number)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(firm);

        // Grouped by which account the other side lands in, and netted within each. A
        // count that finds three of one product and loses two of another has one line
        // of variance and one of loss, not five lines that a reader has to add up.
        Dictionary<StockAccount, decimal> byAccount = [];
        decimal inventory = 0m;

        foreach (StockLedgerEntry movement in movements)
        {
            if (CounterAccountFor(document.Type, movement.Quantity > 0m) is not { } account)
            {
                continue;
            }

            byAccount[account] = byAccount.GetValueOrDefault(account) + movement.Value.Amount;
            inventory += movement.Value.Amount;
        }

        // Nothing to say. A transfer, or a count that agreed with the books: the goods
        // are where they were and worth what they were.
        if (inventory == 0m && byAccount.Values.All(amount => amount == 0m))
        {
            return Result.Success<Voucher?>(null);
        }

        Result<Voucher> draft = Voucher.CreateDraft(
            document.TenantId,
            document.FirmId,
            branchId,
            year,
            VoucherType.Journal,
            number,
            document.Date,
            firm.BaseCurrency,
            firm.BaseCurrency);

        if (draft.IsFailure)
        {
            return Result.Failure<Voucher?>(draft.Error);
        }

        Voucher journal = draft.Value;

        Result<LedgerId> inventoryLedger = map.For(StockAccount.Inventory);

        if (inventoryLedger.IsFailure)
        {
            return Result.Failure<Voucher?>(inventoryLedger.Error);
        }

        Result posted = AddLine(
            journal, inventoryLedger.Value, inventory, $"{document.Type} {document.Number}");

        if (posted.IsFailure)
        {
            return Result.Failure<Voucher?>(posted.Error);
        }

        foreach ((StockAccount account, decimal amount) in byAccount.OrderBy(pair => pair.Key))
        {
            Result<LedgerId> counter = map.For(account);

            if (counter.IsFailure)
            {
                return Result.Failure<Voucher?>(counter.Error);
            }

            // The other side, so the sign is inverted: goods into stock come out of
            // whatever account paid for them.
            Result counterPosted = AddLine(
                journal, counter.Value, -amount, $"{document.Type} {document.Number}");

            if (counterPosted.IsFailure)
            {
                return Result.Failure<Voucher?>(counterPosted.Error);
            }
        }

        return Result.Success<Voucher?>(journal);
    }

    /// <summary>Adds one side of the journal, if it carries anything.</summary>
    /// <remarks>
    /// A movement worth nothing raises no line. Goods can genuinely be worth nothing -
    /// stock found on a first count that nobody has ever costed - and a zero line would
    /// be refused by the voucher, which is right: it would print as a row saying an
    /// account was untouched.
    /// </remarks>
    private static Result AddLine(
        Voucher journal,
        LedgerId ledgerId,
        decimal amount,
        string narration)
    {
        if (amount == 0m)
        {
            return Result.Success();
        }

        Result<VoucherLine> line = journal.AddLine(
            ledgerId,
            amount > 0m ? EntrySide.Debit : EntrySide.Credit,
            Math.Abs(decimal.Round(amount, 2, MidpointRounding.AwayFromZero)),
            narration);

        return line.IsFailure ? Result.Failure(line.Error) : Result.Success();
    }
}
