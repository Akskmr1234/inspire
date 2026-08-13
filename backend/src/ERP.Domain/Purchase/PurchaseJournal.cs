using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>Turns a posted purchase into what it did to the accounts.</summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="Sales.SalesJournal"/>, and the second half of the goods
/// received model the business chose on 2026-08-13. The receipt has already debited
/// inventory and credited Goods Received; this debits Goods Received back, debits the
/// input tax head by head as the supplier charged it, and credits the supplier what the
/// firm owes. The two halves cancel, and what is left in the clearing account is exactly
/// what has arrived and not yet been invoiced.
/// </para>
/// <para>
/// The goods side lands in that clearing account in both directions, which is where this
/// stops mirroring the sales journal. A sales return needs a contra-revenue account of its
/// own because gross sales and returns are both figures somebody reports; a purchase
/// return needs none, because the goods side of a purchase never reaches an income
/// statement account here. It reaches a liability that nets to zero, and netting it is the
/// whole point.
/// </para>
/// <para>
/// What is <em>not</em> here is the cost of the goods reaching inventory. That is the
/// receipt's own journal, raised by the stock document, and posting it here as well would
/// state the same goods twice.
/// </para>
/// </remarks>
public static class PurchaseJournal
{
    /// <summary>Raises the journal a posted purchase owes the nominal ledger.</summary>
    /// <param name="invoice">The purchase, already posted.</param>
    /// <param name="accounts">The firm's account map, for goods received and rounding.</param>
    /// <param name="taxAccounts">The firm's tax account map, for the heads charged.</param>
    /// <param name="firm">The firm, for its base currency.</param>
    /// <param name="year">The financial year the journal falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <returns>The journal, as a draft, or the first reason it cannot be raised.</returns>
    /// <remarks>
    /// Returned as a draft for the caller to post, as the sales journal is: posting is
    /// what puts it into the balances, and that belongs in the transaction that is also
    /// moving the stock and raising the bill.
    /// </remarks>
    public static Result<Voucher> Raise(
        PurchaseInvoice invoice,
        InventoryAccountMap accounts,
        TaxAccountMap taxAccounts,
        Firm firm,
        FinancialYear year,
        string number)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(taxAccounts);
        ArgumentNullException.ThrowIfNull(firm);

        Result usable = EnsureUsable(invoice, accounts, taxAccounts, firm);

        if (usable.IsFailure)
        {
            return Result.Failure<Voucher>(usable.Error);
        }

        Result<Voucher> draft = Voucher.CreateDraft(
            invoice.TenantId,
            invoice.FirmId,
            invoice.BranchId,
            year,
            VoucherType.Journal,
            number,
            invoice.Date,
            invoice.Currency,
            firm.BaseCurrency);

        if (draft.IsFailure)
        {
            return draft;
        }

        Voucher journal = draft.Value;

        string narration = invoice.IsReturn
            ? $"Purchase return {invoice.Number}"
            : $"Purchase invoice {invoice.Number}";

        // A return is the same journal with every side swapped: the supplier is debited
        // what is no longer owed, the goods come back off the clearing account, and the
        // input tax comes back out of what was reclaimable. One piece of arithmetic
        // instead of two that could drift apart, and a journal that balances in one
        // direction balances in the other by construction.
        decimal sign = invoice.IsReturn ? -1m : 1m;

        // What the supplier is owed, which is the document total: the one figure that was
        // rounded, and the one the bill or the debit note will be raised for.
        Result credited = AddLine(
            journal, invoice.SupplierLedgerId, -(sign * invoice.Total.Amount), narration);

        if (credited.IsFailure)
        {
            return Result.Failure<Voucher>(credited.Error);
        }

        // Everything on the other side is accumulated as it is posted, so what is left
        // over at the end is exactly the rounding - see below.
        decimal debited = 0m;

        Result<decimal> goods = AddGoods(journal, invoice, accounts, sign, narration);

        if (goods.IsFailure)
        {
            return Result.Failure<Voucher>(goods.Error);
        }

        debited += goods.Value;

        Result<decimal> tax = AddTax(journal, invoice, taxAccounts, sign, narration);

        if (tax.IsFailure)
        {
            return Result.Failure<Voucher>(tax.Error);
        }

        debited += tax.Value;

        Result<decimal> charges = AddCharges(journal, invoice, sign, narration);

        if (charges.IsFailure)
        {
            return Result.Failure<Voucher>(charges.Error);
        }

        debited += charges.Value;

        Result rounded = AddRoundOff(
            journal, invoice, accounts, (sign * invoice.Total.Amount) - debited, narration);

        return rounded.IsFailure
            ? Result.Failure<Voucher>(rounded.Error)
            : Result.Success(journal);
    }

    /// <summary>Checks the things that would make a journal wrong rather than merely fail.</summary>
    private static Result EnsureUsable(
        PurchaseInvoice invoice,
        InventoryAccountMap accounts,
        TaxAccountMap taxAccounts,
        Firm firm)
    {
        if (invoice.Status != PurchaseInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseJournal.NotPosted",
                $"Purchase '{invoice.Number}' is {invoice.Status}, and a document that has "
                + "not posted owes the ledger nothing."));
        }

        if (accounts.FirmId != invoice.FirmId || taxAccounts.FirmId != invoice.FirmId)
        {
            return Result.Failure(Error.Validation(
                "PurchaseJournal.MapNotInFirm",
                "The account maps belong to another firm than the purchase."));
        }

        // The document carries no exchange rate, so there is nothing to convert a
        // foreign-currency purchase into the books with. Refused by name rather than
        // posted at a rate of one, which would state a hundred dollars as a hundred riyals
        // and reconcile perfectly while being wrong. Imports are exactly where this bites,
        // which is why it is refused loudly.
        return invoice.Currency == firm.BaseCurrency
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "PurchaseJournal.CurrencyNotBase",
                $"Purchase '{invoice.Number}' is in {invoice.Currency} and the firm keeps "
                + $"its books in {firm.BaseCurrency}. A purchase in another currency needs "
                + "an exchange rate, which the document does not yet carry."));
    }

    /// <summary>Clears the goods off the account the receipt parked them in.</summary>
    /// <returns>What was posted, so the caller can find the rounding.</returns>
    /// <remarks>
    /// The same account in both directions. Goods Received is a clearing account, so a
    /// return credits back exactly what the purchase debited and neither figure is one
    /// anybody reports gross - which is why there is no contra account here answering to
    /// the sales side's Sales Returns.
    /// </remarks>
    private static Result<decimal> AddGoods(
        Voucher journal,
        PurchaseInvoice invoice,
        InventoryAccountMap accounts,
        decimal sign,
        string narration)
    {
        Result<LedgerId> received = accounts.For(StockAccount.GoodsReceived);

        if (received.IsFailure)
        {
            return Result.Failure<decimal>(received.Error);
        }

        decimal amount = sign * Money.Of(invoice.Taxable.Amount, invoice.Currency).Amount;
        Result posted = AddLine(journal, received.Value, amount, narration);

        return posted.IsFailure
            ? Result.Failure<decimal>(posted.Error)
            : Result.Success(amount);
    }

    /// <summary>Debits each head the supplier charged to the account the firm chose for it.</summary>
    /// <remarks>
    /// Summed per head across the lines rather than posted line by line, as the output
    /// side is: a return asks what is reclaimable under each head, not what each line
    /// contributed.
    /// </remarks>
    private static Result<decimal> AddTax(
        Voucher journal,
        PurchaseInvoice invoice,
        TaxAccountMap taxAccounts,
        decimal sign,
        string narration)
    {
        Dictionary<TaxComponentType, decimal> byHead = [];

        foreach (PurchaseInvoiceLine line in invoice.Lines)
        {
            foreach (PurchaseInvoiceLineTax component in line.Components)
            {
                byHead[component.Type] =
                    byHead.GetValueOrDefault(component.Type) + component.Amount;
            }
        }

        decimal debited = 0m;

        foreach ((TaxComponentType head, decimal raw) in byHead.OrderBy(pair => pair.Key))
        {
            decimal amount = sign * Money.Of(raw, invoice.Currency).Amount;

            // A head charged at nothing needs no account, for the reason the output side
            // skips one: a zero-rated purchase is a real thing, and refusing it because
            // nobody chose an account for a tax that was never charged would be refusing
            // it for a question that never arose.
            if (amount == 0m)
            {
                continue;
            }

            Result<LedgerId> ledger = taxAccounts.For(head, TaxDirection.Input);

            if (ledger.IsFailure)
            {
                return Result.Failure<decimal>(ledger.Error);
            }

            Result posted = AddLine(journal, ledger.Value, amount, narration);

            if (posted.IsFailure)
            {
                return Result.Failure<decimal>(posted.Error);
            }

            debited += amount;
        }

        return Result.Success(debited);
    }

    /// <summary>Posts the charges carried beside the goods to their own ledgers.</summary>
    /// <remarks>
    /// Each to the ledger the firm's charge matrix named. Note what they do <em>not</em>
    /// do: freight a supplier bills on a purchase is charged to freight, not added to the
    /// cost of the goods. Landed costing - spreading a carriage charge across the units it
    /// carried - is a separate thing the specification does not yet ask for, and doing it
    /// halfway here would put a figure into stock valuation that no report could explain.
    /// </remarks>
    private static Result<decimal> AddCharges(
        Voucher journal,
        PurchaseInvoice invoice,
        decimal sign,
        string narration)
    {
        decimal debited = 0m;

        foreach (PurchaseInvoiceCharge charge in invoice.Charges)
        {
            // An addition is money the firm pays on top, so it is debited; a deduction is
            // a discount the supplier gave, so it is credited. The sign the charge already
            // carries says which, and the document's own sign says whether the whole thing
            // runs forwards or backwards.
            decimal amount = sign * charge.SignedAmount.Amount;
            Result posted = AddLine(journal, charge.LedgerId, amount, narration);

            if (posted.IsFailure)
            {
                return Result.Failure<decimal>(posted.Error);
            }

            debited += amount;
        }

        return Result.Success(debited);
    }

    /// <summary>Puts whatever the rounding left over into the account chosen for it.</summary>
    /// <remarks>
    /// The residual rather than the document's own rounding difference, and they are the
    /// same figure whenever the components were already at the currency's scale. Where
    /// they are not - tax held per component at full precision - the difference between
    /// the rounded total and the sum of the rounded parts has to land somewhere for the
    /// journal to balance at all.
    /// </remarks>
    private static Result AddRoundOff(
        Voucher journal,
        PurchaseInvoice invoice,
        InventoryAccountMap accounts,
        decimal residual,
        string narration)
    {
        if (Money.Of(residual, invoice.Currency).Amount == 0m)
        {
            return Result.Success();
        }

        Result<LedgerId> roundOff = accounts.For(StockAccount.RoundOff);

        return roundOff.IsFailure
            ? Result.Failure(roundOff.Error)
            : AddLine(journal, roundOff.Value, residual, narration);
    }

    /// <summary>Adds one side of the journal, if it carries anything.</summary>
    /// <remarks>
    /// Positive debits and negative credits, so the arithmetic above can be written as one
    /// running figure rather than as two that have to be compared at the end. A line worth
    /// nothing is skipped: the voucher would refuse it, and rightly, because it prints as
    /// a row saying an account was untouched.
    /// </remarks>
    private static Result AddLine(
        Voucher journal,
        LedgerId ledgerId,
        decimal amount,
        string narration)
    {
        decimal rounded = decimal.Round(
            amount, journal.Currency.DecimalPlaces, MidpointRounding.AwayFromZero);

        if (rounded == 0m)
        {
            return Result.Success();
        }

        Result<VoucherLine> line = journal.AddLine(
            ledgerId,
            rounded > 0m ? EntrySide.Debit : EntrySide.Credit,
            Math.Abs(rounded),
            narration);

        return line.IsFailure ? Result.Failure(line.Error) : Result.Success();
    }
}
