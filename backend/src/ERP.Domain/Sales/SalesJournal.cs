using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>Turns a posted invoice into what it did to the accounts.</summary>
/// <remarks>
/// <para>
/// The posting the two account maps were built for. Revenue is credited to the one sales
/// account the firm chose; tax is credited head by head as the engine assessed it, which
/// is what lets a single posting produce a VAT return in Doha and a GST return in Kochi;
/// and the customer is debited what they actually owe.
/// </para>
/// <para>
/// What is <em>not</em> here is the cost of the goods. A sale issues stock through a
/// document of its own, and that issue raises its own journal debiting cost of goods sold
/// and crediting inventory. Posting the cost here as well would state it twice, and the
/// margin - revenue less cost - would read as double what the firm made.
/// </para>
/// <para>
/// In the domain rather than beside the stock journal in the application layer, because
/// it needs nothing an application layer has. A stock journal is built from ledger
/// entries the posting service produced; this is a function of the invoice and the firm's
/// two maps, which are aggregates, so it can be read - and tested - without a repository
/// anywhere near it.
/// </para>
/// </remarks>
public static class SalesJournal
{
    /// <summary>Raises the journal a posted invoice owes the nominal ledger.</summary>
    /// <param name="invoice">The invoice, already posted.</param>
    /// <param name="accounts">The firm's account map, for revenue and rounding.</param>
    /// <param name="taxAccounts">The firm's tax account map, for the heads charged.</param>
    /// <param name="firm">The firm, for its base currency.</param>
    /// <param name="year">The financial year the journal falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <returns>The journal, as a draft, or the first reason it cannot be raised.</returns>
    /// <remarks>
    /// Returned as a draft for the caller to post, in the same way the stock journal is:
    /// posting is what puts it into the balances, and that belongs in the transaction
    /// that is also moving the stock and raising the bill.
    /// </remarks>
    public static Result<Voucher> Raise(
        SalesInvoice invoice,
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
            ? $"Sales return {invoice.Number}"
            : $"Sales invoice {invoice.Number}";

        // A return is the same journal with every side swapped, which is what it is: the
        // customer is credited what they no longer owe, the goods come off revenue, and
        // the tax comes back out of the liability. Expressing it as a sign keeps one
        // piece of arithmetic instead of two that could drift apart, and a journal that
        // balances in one direction balances in the other by construction.
        decimal sign = invoice.IsReturn ? -1m : 1m;

        // What the customer owes, which is the document total: the one figure that was
        // rounded, and the one the bill or the credit will be raised for.
        Result debited = AddLine(
            journal, invoice.CustomerLedgerId, sign * invoice.Total.Amount, narration);

        if (debited.IsFailure)
        {
            return Result.Failure<Voucher>(debited.Error);
        }

        // Everything on the other side is accumulated as it is posted, so what is left
        // over at the end is exactly the rounding - see below.
        decimal credited = 0m;

        Result<decimal> revenue = AddRevenue(journal, invoice, accounts, sign, narration);

        if (revenue.IsFailure)
        {
            return Result.Failure<Voucher>(revenue.Error);
        }

        credited += revenue.Value;

        Result<decimal> tax = AddTax(journal, invoice, taxAccounts, sign, narration);

        if (tax.IsFailure)
        {
            return Result.Failure<Voucher>(tax.Error);
        }

        credited += tax.Value;

        Result<decimal> charges = AddCharges(journal, invoice, sign, narration);

        if (charges.IsFailure)
        {
            return Result.Failure<Voucher>(charges.Error);
        }

        credited += charges.Value;

        Result rounded = AddRoundOff(
            journal, invoice, accounts, (sign * invoice.Total.Amount) - credited, narration);

        return rounded.IsFailure ? Result.Failure<Voucher>(rounded.Error) : Result.Success(journal);
    }

    /// <summary>Checks the things that would make a journal wrong rather than merely fail.</summary>
    private static Result EnsureUsable(
        SalesInvoice invoice,
        InventoryAccountMap accounts,
        TaxAccountMap taxAccounts,
        Firm firm)
    {
        if (invoice.Status != SalesInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesJournal.NotPosted",
                $"Invoice '{invoice.Number}' is {invoice.Status}, and a document that has "
                + "not posted owes the ledger nothing."));
        }

        if (accounts.FirmId != invoice.FirmId || taxAccounts.FirmId != invoice.FirmId)
        {
            return Result.Failure(Error.Validation(
                "SalesJournal.MapNotInFirm",
                "The account maps belong to another firm than the invoice."));
        }

        // The invoice carries no exchange rate - section 12.3's ERate column is not built
        // - so there is nothing to convert a foreign-currency sale into the books with.
        // Refused by name rather than posted at a rate of one, which would state a
        // hundred dollars as a hundred riyals and reconcile perfectly while being wrong.
        return invoice.Currency == firm.BaseCurrency
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "SalesJournal.CurrencyNotBase",
                $"Invoice '{invoice.Number}' is in {invoice.Currency} and the firm keeps "
                + $"its books in {firm.BaseCurrency}. A sale in another currency needs an "
                + "exchange rate, which an invoice does not yet carry."));
    }

    /// <summary>Credits the goods to the firm's sales account, or takes them back off it.</summary>
    /// <returns>What was posted, so the caller can find the rounding.</returns>
    /// <remarks>
    /// A return goes to its own contra-revenue account rather than back to sales, which
    /// is the business's answer of 2026-08-12: net revenue is the same either way, and
    /// what differs is whether the gross figure and the returns are still separately
    /// answerable from the chart afterwards.
    /// </remarks>
    private static Result<decimal> AddRevenue(
        Voucher journal,
        SalesInvoice invoice,
        InventoryAccountMap accounts,
        decimal sign,
        string narration)
    {
        Result<LedgerId> sales = accounts.For(
            invoice.IsReturn ? StockAccount.SalesReturn : StockAccount.SalesRevenue);

        if (sales.IsFailure)
        {
            return Result.Failure<decimal>(sales.Error);
        }

        decimal amount = sign * Money.Of(invoice.Taxable.Amount, invoice.Currency).Amount;
        Result posted = AddLine(journal, sales.Value, -amount, narration);

        return posted.IsFailure ? Result.Failure<decimal>(posted.Error) : Result.Success(amount);
    }

    /// <summary>Credits each head the engine assessed to the account the firm chose for it.</summary>
    /// <remarks>
    /// Summed per head across the lines rather than posted line by line. A return asks
    /// what a firm owes under each head, not what each line contributed, and forty lines
    /// would put forty rows of output VAT into a journal somebody has to read.
    /// </remarks>
    private static Result<decimal> AddTax(
        Voucher journal,
        SalesInvoice invoice,
        TaxAccountMap taxAccounts,
        decimal sign,
        string narration)
    {
        Dictionary<TaxComponentType, decimal> byHead = [];

        foreach (SalesInvoiceLine line in invoice.Lines)
        {
            foreach (SalesInvoiceLineTax component in line.Components)
            {
                byHead[component.Type] =
                    byHead.GetValueOrDefault(component.Type) + component.Amount;
            }
        }

        decimal credited = 0m;

        foreach ((TaxComponentType head, decimal raw) in byHead.OrderBy(pair => pair.Key))
        {
            decimal amount = sign * Money.Of(raw, invoice.Currency).Amount;

            // A head assessed at nothing needs no account. A zero-rated line is a real
            // thing - exports, exempt goods - and refusing the invoice because nobody
            // chose an account for a tax it did not actually charge would be refusing it
            // for a question that never arose.
            if (amount == 0m)
            {
                continue;
            }

            Result<LedgerId> ledger = taxAccounts.For(head, TaxDirection.Output);

            if (ledger.IsFailure)
            {
                return Result.Failure<decimal>(ledger.Error);
            }

            Result posted = AddLine(journal, ledger.Value, -amount, narration);

            if (posted.IsFailure)
            {
                return Result.Failure<decimal>(posted.Error);
            }

            credited += amount;
        }

        return Result.Success(credited);
    }

    /// <summary>Posts the charges carried beside the goods to their own ledgers.</summary>
    /// <remarks>
    /// Each to the ledger the firm's charge matrix named, which is why the charge holds a
    /// ledger of its own rather than a kind: freight and packing are different lines in
    /// somebody's accounts even when they behave identically on the invoice.
    /// </remarks>
    private static Result<decimal> AddCharges(
        Voucher journal,
        SalesInvoice invoice,
        decimal sign,
        string narration)
    {
        decimal credited = 0m;

        foreach (SalesInvoiceCharge charge in invoice.Charges)
        {
            // An addition is money the customer pays on top, so it is credited; a
            // deduction is a discount the firm gives away, so it is debited. The sign
            // the charge already carries says which, and the document's own sign says
            // whether the whole thing runs forwards or backwards.
            decimal amount = sign * charge.SignedAmount.Amount;
            Result posted = AddLine(journal, charge.LedgerId, -amount, narration);

            if (posted.IsFailure)
            {
                return Result.Failure<decimal>(posted.Error);
            }

            credited += amount;
        }

        return Result.Success(credited);
    }

    /// <summary>Puts whatever the rounding left over into the account chosen for it.</summary>
    /// <remarks>
    /// The residual rather than the invoice's own rounding difference, and they are the
    /// same figure whenever the components were already at the currency's scale. Where
    /// they are not - tax held per component at full precision, which is the whole point
    /// of holding it that way - the difference between the rounded total and the sum of
    /// the rounded parts is a few thousandths that has to land somewhere for the journal
    /// to balance at all. Round Off is where the business said it goes.
    /// </remarks>
    private static Result AddRoundOff(
        Voucher journal,
        SalesInvoice invoice,
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
            : AddLine(journal, roundOff.Value, -residual, narration);
    }

    /// <summary>Adds one side of the journal, if it carries anything.</summary>
    /// <remarks>
    /// Positive debits and negative credits, so the arithmetic above can be written as
    /// one running figure rather than as two that have to be compared at the end. A line
    /// worth nothing is skipped: the voucher would refuse it, and rightly, because it
    /// prints as a row saying an account was untouched.
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
