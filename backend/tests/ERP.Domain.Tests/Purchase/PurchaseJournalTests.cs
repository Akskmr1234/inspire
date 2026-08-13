using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Purchase;

/// <summary>Tests for <see cref="PurchaseJournal"/>: what a purchase does to the accounts.</summary>
/// <remarks>
/// The other half of the goods received model. What these are really checking is that the
/// clearing account nets to nothing when a delivery and its invoice both land, that the
/// input tax is reclaimed head by head under two tax systems, and that the whole thing
/// runs backwards on a return without a second piece of arithmetic to keep in step.
/// </remarks>
public sealed class PurchaseJournalTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId FirmKey = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateOnly Date = new(2026, 8, 13);
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_purchase_credits_the_supplier_and_debits_the_goods_and_its_tax()
    {
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        // The firm owes the lot; the goods clear off the account the receipt parked them
        // in; the tax is reclaimable.
        Credit(journal, invoice.SupplierLedgerId).ShouldBe(1_050m);
        Debit(journal, books.AccountFor(StockAccount.GoodsReceived)).ShouldBe(1_000m);
        Debit(journal, books.TaxLedger(TaxComponentType.Vat)).ShouldBe(50m);

        journal.Lines.Count.ShouldBe(3);
    }

    [Fact]
    public void The_goods_reaching_inventory_are_left_to_the_receipt_that_moved_them()
    {
        // A purchase posts twice: this journal, and the receipt's own, which debits
        // inventory and credits goods received. If inventory were debited here as well
        // the stock would be valued at twice what the firm paid.
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        Posted(journal, books.AccountFor(StockAccount.Inventory)).ShouldBe(0m);
    }

    [Fact]
    public void The_clearing_account_nets_to_nothing_once_both_halves_have_landed()
    {
        // The whole reason for the account. The receipt credits it what arrived and the
        // invoice debits it back; a firm that receives and invoices everything ends the
        // period with nothing in it, and whatever is left is goods received and not yet
        // invoiced.
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        decimal invoiced = Debit(journal, books.AccountFor(StockAccount.GoodsReceived));
        decimal receipted = invoice.Taxable.Amount;

        (invoiced - receipted).ShouldBe(0m);
    }

    [Fact]
    public void A_GST_purchase_debits_each_head_to_the_input_account_chosen_for_it()
    {
        // The input mirror of the output side: the same code reclaims Input VAT in Doha
        // and CGST and SGST in Kochi, because the engine says which heads applied and the
        // map says where each one lands.
        Books books = Books.ForGst();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 180m);

        Voucher journal = books.Raise(invoice).Value;

        Debit(journal, books.TaxLedger(TaxComponentType.Cgst)).ShouldBe(90m);
        Debit(journal, books.TaxLedger(TaxComponentType.Sgst)).ShouldBe(90m);
        Credit(journal, invoice.SupplierLedgerId).ShouldBe(1_180m);
    }

    [Fact]
    public void The_input_tax_goes_to_the_input_account_and_not_the_output_one()
    {
        // The one substitution that would reconcile perfectly and be wrong: reclaiming
        // input tax against the output liability nets the return to the right figure
        // while making both halves of it unreportable.
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        Posted(journal, books.TaxLedger(TaxComponentType.Vat)).ShouldBe(50m);
        Posted(journal, books.OutputTaxLedger(TaxComponentType.Vat)).ShouldBe(0m);
    }

    [Fact]
    public void A_charge_that_adds_is_debited_and_one_that_deducts_is_credited()
    {
        Books books = Books.ForVat();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);
        AdditionalLedger discount = Books.Charge("DISC-RECEIVED", isAddition: false);

        PurchaseInvoice invoice = books.Buy(
            taxable: 1_000m, tax: 50m, charges: [(freight, 30m), (discount, 20m)]);

        Voucher journal = books.Raise(invoice).Value;

        // Freight is money the firm pays on top; a discount is money the supplier gave
        // back. The document already knows which way each moves the total.
        Debit(journal, freight.LedgerId).ShouldBe(30m);
        Credit(journal, discount.LedgerId).ShouldBe(20m);
        Credit(journal, invoice.SupplierLedgerId).ShouldBe(1_060m);
    }

    [Fact]
    public void Freight_on_a_purchase_is_charged_rather_than_added_to_the_cost_of_stock()
    {
        // Recorded rather than assumed. Landed costing - spreading a carriage charge
        // across the units it carried - is a separate thing the specification does not
        // ask for yet, so freight is an expense and the stock is valued at what the goods
        // themselves cost.
        Books books = Books.ForVat();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);

        PurchaseInvoice invoice = books.Buy(1_000m, 50m, charges: [(freight, 30m)]);

        Voucher journal = books.Raise(invoice).Value;

        Debit(journal, freight.LedgerId).ShouldBe(30m);
        Debit(journal, books.AccountFor(StockAccount.GoodsReceived)).ShouldBe(1_000m);
    }

    [Fact]
    public void A_journal_balances_whatever_it_carries()
    {
        // The property that matters more than any individual figure: a voucher that does
        // not balance cannot post, so an unbalanced journal is a purchase that cannot be
        // entered rather than books that are quietly wrong.
        Books books = Books.ForGst();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);
        AdditionalLedger discount = Books.Charge("DISC-RECEIVED", isAddition: false);

        PurchaseInvoice invoice = books.Buy(
            taxable: 333.33m,
            tax: 59.99m,
            charges: [(freight, 12.35m), (discount, 7.77m)]);

        Voucher journal = books.Raise(invoice).Value;

        Balances(journal).ShouldBeTrue();
        journal.Post(User, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_reaches_round_off_while_nothing_is_lost()
    {
        // Round Off carries the residual between the rounded total and the sum of the
        // rounded parts. Today that residual is always nil, for the reason it is nil on
        // the sales side: the engine returns every figure at the currency's own scale and
        // the document refuses a line finer than that. The line stays because it is what
        // makes the journal balance by construction rather than by luck.
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        Posted(journal, books.AccountFor(StockAccount.RoundOff)).ShouldBe(0m);
        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_return_runs_the_same_journal_with_every_side_swapped()
    {
        Books books = Books.ForVat();
        PurchaseInvoice note = books.Buy(1_000m, 50m, kind: PurchaseDocumentKind.Return);

        Voucher journal = books.Raise(note).Value;

        Debit(journal, note.SupplierLedgerId).ShouldBe(1_050m);
        Credit(journal, books.AccountFor(StockAccount.GoodsReceived)).ShouldBe(1_000m);
        Credit(journal, books.TaxLedger(TaxComponentType.Vat)).ShouldBe(50m);

        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_return_clears_the_same_account_rather_than_a_contra_one()
    {
        // Where this stops mirroring the sales side. A sales return needs its own
        // contra-revenue account because gross sales and returns are both figures
        // somebody reports; a purchase return needs none, because the goods side of a
        // purchase lands in a liability that is meant to net to zero.
        Books books = Books.ForVat();
        PurchaseInvoice note = books.Buy(1_000m, 50m, kind: PurchaseDocumentKind.Return);

        Voucher journal = books.Raise(note).Value;

        Posted(journal, books.AccountFor(StockAccount.GoodsReceived)).ShouldBe(1_000m);
        Posted(journal, books.AccountFor(StockAccount.SalesReturn)).ShouldBe(0m);
    }

    [Fact]
    public void A_GST_return_takes_each_head_back_out_of_what_was_reclaimable()
    {
        Books books = Books.ForGst();
        PurchaseInvoice note = books.Buy(1_000m, 180m, kind: PurchaseDocumentKind.Return);

        Voucher journal = books.Raise(note).Value;

        Credit(journal, books.TaxLedger(TaxComponentType.Cgst)).ShouldBe(90m);
        Credit(journal, books.TaxLedger(TaxComponentType.Sgst)).ShouldBe(90m);
        Debit(journal, note.SupplierLedgerId).ShouldBe(1_180m);
    }

    [Fact]
    public void A_charge_on_a_return_swaps_with_everything_else()
    {
        // Freight on a debit note is freight the firm is getting back, so it runs the
        // other way too. Anything else would leave the journal unbalanced by twice it.
        Books books = Books.ForVat();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);

        PurchaseInvoice note = books.Buy(
            1_000m, 50m, charges: [(freight, 30m)], kind: PurchaseDocumentKind.Return);

        Voucher journal = books.Raise(note).Value;

        Credit(journal, freight.LedgerId).ShouldBe(30m);
        Debit(journal, note.SupplierLedgerId).ShouldBe(1_080m);
        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_firm_that_has_not_chosen_a_goods_received_account_cannot_post_one()
    {
        Books books = Books.ForVat(assignGoods: false);
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Result<Voucher> raised = books.Raise(invoice);

        raised.Error.Code.ShouldBe("InventoryAccounts.NotConfigured");
        raised.Error.Description.ShouldContain("not yet invoiced");
    }

    [Fact]
    public void A_head_with_no_input_account_stops_the_purchase_and_names_the_head()
    {
        // The refusal the map exists for. A firm paying a tax it has nowhere to reclaim
        // is a firm whose return will not reconcile, and the first purchase is the
        // cheapest moment for somebody to find that out.
        Books books = Books.ForGst(assignTax: false);
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 180m);

        Result<Voucher> raised = books.Raise(invoice);

        raised.Error.Code.ShouldBe("TaxAccounts.NotConfigured");
        raised.Error.Description.ShouldContain("Cgst");
    }

    [Fact]
    public void A_zero_rated_purchase_needs_no_tax_account_at_all()
    {
        // Exempt and zero-rated goods are a real thing, and a firm that buys nothing else
        // should not have to choose an account for a tax it never pays.
        Books books = Books.ForVat(assignTax: false);
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 0m);

        Voucher journal = books.Raise(invoice).Value;

        Credit(journal, invoice.SupplierLedgerId).ShouldBe(1_000m);
        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_draft_owes_the_ledger_nothing()
    {
        Books books = Books.ForVat();
        PurchaseInvoice draft = books.Draft();

        books.Raise(draft).Error.Code.ShouldBe("PurchaseJournal.NotPosted");
    }

    [Fact]
    public void A_purchase_in_a_currency_the_firm_does_not_keep_its_books_in_is_refused()
    {
        // Where this bites hardest: an import is a purchase in somebody else's currency,
        // and posting it at a rate of one would state a hundred dollars as a hundred
        // riyals and reconcile perfectly while being wrong.
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(1_000m, 50m, currency: CurrencyCode.Usd);

        books.Raise(invoice).Error.Code.ShouldBe("PurchaseJournal.CurrencyNotBase");
    }

    [Fact]
    public void A_map_belonging_to_another_firm_cannot_post_this_firm_s_purchase()
    {
        Books books = Books.ForVat();
        PurchaseInvoice invoice = books.Buy(taxable: 1_000m, tax: 50m);

        Result<Voucher> raised = PurchaseJournal.Raise(
            invoice,
            InventoryAccountMap.Create(Tenant, FirmId.NewId()),
            books.TaxAccounts,
            books.Firm,
            books.Year,
            "JV/2026/0001");

        raised.Error.Code.ShouldBe("PurchaseJournal.MapNotInFirm");
    }

    // ------------------------------------------------------------------ assertions

    private static decimal Debit(Voucher journal, LedgerId ledger) =>
        journal.Lines
            .Where(line => line.LedgerId == ledger && line.Side == EntrySide.Debit)
            .Sum(line => line.Amount.Amount);

    private static decimal Credit(Voucher journal, LedgerId ledger) =>
        journal.Lines
            .Where(line => line.LedgerId == ledger && line.Side == EntrySide.Credit)
            .Sum(line => line.Amount.Amount);

    private static decimal Posted(Voucher journal, LedgerId ledger) =>
        Debit(journal, ledger) + Credit(journal, ledger);

    private static bool Balances(Voucher journal) =>
        journal.Lines.Where(line => line.Side == EntrySide.Debit).Sum(line => line.Amount.Amount)
        == journal.Lines.Where(line => line.Side == EntrySide.Credit).Sum(line => line.Amount.Amount);

    // ------------------------------------------------------------------ a firm's books

    /// <summary>A firm, its chart, and the two maps that say where a purchase posts.</summary>
    /// <remarks>
    /// Assembled once per test rather than shared, so a test that repoints an account or
    /// leaves one unassigned cannot affect another. Both directions of the tax map are
    /// filled, because a firm that buys also sells and the input account being the right
    /// one is a thing worth being able to check.
    /// </remarks>
    private sealed class Books
    {
        private readonly Dictionary<StockAccount, Ledger> _ledgers = [];
        private readonly Dictionary<TaxComponentType, Ledger> _inputLedgers = [];
        private readonly Dictionary<TaxComponentType, Ledger> _outputLedgers = [];
        private readonly TaxRegime _regime;

        private Books(TaxRegime regime, bool assignGoods, bool assignTax)
        {
            _regime = regime;

            Firm = ERP.Domain.Tenancy.Firm.Create(
                Tenant, "MAIN", "Inspire Trading", Qar, regime, "Asia/Qatar").Value;

            Accounts = InventoryAccountMap.Create(Tenant, FirmKey);
            TaxAccounts = TaxAccountMap.Create(Tenant, FirmKey);

            foreach (StockAccount account in Enum.GetValues<StockAccount>())
            {
                Ledger ledger = LedgerNamed($"{account}", LedgerKind.General);
                _ledgers[account] = ledger;

                if (account != StockAccount.GoodsReceived || assignGoods)
                {
                    Accounts.Assign(account, ledger).IsSuccess.ShouldBeTrue();
                }
            }

            foreach (TaxComponentType head in Heads())
            {
                Ledger input = LedgerNamed($"{head}-INPUT", LedgerKind.Tax);
                Ledger output = LedgerNamed($"{head}-OUTPUT", LedgerKind.Tax);
                _inputLedgers[head] = input;
                _outputLedgers[head] = output;

                TaxAccounts.Assign(head, TaxDirection.Output, output).IsSuccess.ShouldBeTrue();

                if (assignTax)
                {
                    TaxAccounts.Assign(head, TaxDirection.Input, input)
                        .IsSuccess.ShouldBeTrue();
                }
            }
        }

        public Firm Firm { get; }

        public InventoryAccountMap Accounts { get; }

        public TaxAccountMap TaxAccounts { get; }

        public FinancialYear Year { get; } = FinancialYear.Create(
            Tenant, FirmKey, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [])
            .Value;

        public static Books ForVat(bool assignGoods = true, bool assignTax = true) =>
            new(TaxRegime.GccVat, assignGoods, assignTax);

        public static Books ForGst(bool assignGoods = true, bool assignTax = true) =>
            new(TaxRegime.IndiaGst, assignGoods, assignTax);

        public LedgerId AccountFor(StockAccount account) => _ledgers[account].Id;

        public LedgerId TaxLedger(TaxComponentType head) => _inputLedgers[head].Id;

        public LedgerId OutputTaxLedger(TaxComponentType head) => _outputLedgers[head].Id;

        public Result<Voucher> Raise(PurchaseInvoice invoice) =>
            PurchaseJournal.Raise(invoice, Accounts, TaxAccounts, Firm, Year, "JV/2026/0001");

        public PurchaseInvoice Draft(
            CurrencyCode? currency = null,
            PurchaseDocumentKind kind = PurchaseDocumentKind.Invoice) =>
            PurchaseInvoice.CreateDraft(
                Tenant,
                FirmKey,
                Branch,
                Year,
                "PU/2026/0001",
                Date,
                Supplier(),
                Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
                TaxMode.Tax,
                currency ?? Qar,
                kind).Value;

        /// <summary>Raises and posts a purchase for one line assessed as stated.</summary>
        public PurchaseInvoice Buy(
            decimal taxable,
            decimal tax,
            IReadOnlyList<(AdditionalLedger Charge, decimal Amount)>? charges = null,
            CurrencyCode? currency = null,
            PurchaseDocumentKind kind = PurchaseDocumentKind.Invoice)
        {
            PurchaseInvoice invoice = Draft(currency, kind);
            UnitOfMeasure each = UnitOfMeasure.CreateBase(Tenant, FirmKey, "EACH", "Each").Value;

            invoice.AddLine(
                Stocked(each), each, 1m, 1m, taxable,
                Assessed(taxable, tax, currency ?? Qar)).IsSuccess.ShouldBeTrue();

            foreach ((AdditionalLedger charge, decimal amount) in charges ?? [])
            {
                invoice.AddCharge(charge, amount).IsSuccess.ShouldBeTrue();
            }

            invoice.Post(User, Now).IsSuccess.ShouldBeTrue();

            return invoice;
        }

        public static AdditionalLedger Charge(string code, bool isAddition) =>
            AdditionalLedger.Map(
                Tenant,
                FirmKey,
                ChargeableDocument.Purchase,
                LedgerNamed(code, LedgerKind.AdditionalCharge),
                isAddition).Value;

        /// <summary>The heads the firm's own regime can charge.</summary>
        private IReadOnlyList<TaxComponentType> Heads() => _regime switch
        {
            TaxRegime.IndiaGst =>
                [TaxComponentType.Cgst, TaxComponentType.Sgst, TaxComponentType.Igst],
            _ => [TaxComponentType.Vat],
        };

        /// <summary>Assesses a line the way the application layer will: through the engine.</summary>
        private TaxAssessment Assessed(decimal taxable, decimal tax, CurrencyCode currency) =>
            TaxCalculator.Calculate(
                Money.Of(taxable, currency),
                TaxRate.FromTrusted(
                    taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
                new TaxContext(
                    _regime,
                    DocumentTaxMode.Taxable,
                    AmountsIncludeTax: false,
                    IsInterStateSupply: false));

        private static Ledger Supplier() =>
            Ledger.Create(
                AccountGroup.CreateRoot(
                    Tenant, FirmKey, "G2200", "Creditors", AccountNature.Liability).Value,
                "2200",
                "Gulf Wholesale",
                LedgerKind.Supplier,
                Qar).Value;

        private static Ledger LedgerNamed(string code, LedgerKind kind) =>
            Ledger.Create(
                AccountGroup.CreateRoot(
                    Tenant, FirmKey, $"G{code}", $"Group {code}", AccountNature.Expense).Value,
                code,
                code,
                kind,
                Qar).Value;

        private static Product Stocked(UnitOfMeasure unit) =>
            Product.Create(
                Category.CreateRoot(Tenant, FirmKey, "GEN", "General").Value,
                unit,
                $"PRO-{Guid.NewGuid():N}"[..12],
                "A thing",
                ItemType.Stock,
                Qar).Value;
    }
}
