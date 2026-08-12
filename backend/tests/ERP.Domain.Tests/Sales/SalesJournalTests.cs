using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Sales;

/// <summary>Tests for <see cref="SalesJournal"/>: what a sale does to the accounts.</summary>
/// <remarks>
/// The posting both account maps were built for. What these are really checking is that
/// one arrangement of code produces a correct posting under two tax systems - a VAT firm
/// crediting one head and a GST firm crediting three - and that it balances even when the
/// tax was assessed at a precision the currency does not have.
/// </remarks>
public sealed class SalesJournalTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId FirmKey = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateOnly Date = new(2026, 8, 11);
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_sale_debits_the_customer_and_credits_revenue_and_its_tax()
    {
        Books books = Books.ForVat();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        // The customer owes the lot; the goods are revenue; the tax is the state's.
        Debit(journal, invoice.CustomerLedgerId).ShouldBe(1_050m);
        Credit(journal, books.AccountFor(StockAccount.SalesRevenue)).ShouldBe(1_000m);
        Credit(journal, books.TaxLedger(TaxComponentType.Vat)).ShouldBe(50m);

        journal.Lines.Count.ShouldBe(3);
    }

    [Fact]
    public void The_cost_of_the_goods_is_left_to_the_issue_that_moved_them()
    {
        // A sale posts twice: this journal, and the stock issue's own. If the cost were
        // here as well it would be stated twice and the gross margin would read as
        // double what the firm actually made.
        Books books = Books.ForVat();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        Posted(journal, books.AccountFor(StockAccount.CostOfGoodsSold)).ShouldBe(0m);
        Posted(journal, books.AccountFor(StockAccount.Inventory)).ShouldBe(0m);
    }

    [Fact]
    public void A_GST_sale_credits_each_head_to_the_account_chosen_for_it()
    {
        // The reason tax is stored per component rather than as one figure: the same
        // code that credits Output VAT in Doha credits CGST and SGST in Kochi, because
        // the engine says which heads applied and the map says where each one lands.
        Books books = Books.ForGst();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 180m);

        Voucher journal = books.Raise(invoice).Value;

        Credit(journal, books.TaxLedger(TaxComponentType.Cgst)).ShouldBe(90m);
        Credit(journal, books.TaxLedger(TaxComponentType.Sgst)).ShouldBe(90m);
        Debit(journal, invoice.CustomerLedgerId).ShouldBe(1_180m);
    }

    [Fact]
    public void A_charge_that_adds_is_credited_and_one_that_deducts_is_debited()
    {
        Books books = Books.ForVat();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);
        AdditionalLedger discount = Books.Charge("DISC-ALLOWED", isAddition: false);

        SalesInvoice invoice = books.Sell(
            taxable: 1_000m, tax: 50m, charges: [(freight, 30m), (discount, 20m)]);

        Voucher journal = books.Raise(invoice).Value;

        // Freight is money the customer pays on top; a discount is money the firm gives
        // away. The invoice already knows which way each moves the total.
        Credit(journal, freight.LedgerId).ShouldBe(30m);
        Debit(journal, discount.LedgerId).ShouldBe(20m);
        Debit(journal, invoice.CustomerLedgerId).ShouldBe(1_060m);
    }

    [Fact]
    public void Nothing_reaches_round_off_while_nothing_is_lost()
    {
        // Round Off carries the residual between the rounded total and the sum of the
        // rounded parts. Today that residual is always nil, because the tax engine
        // returns every figure at the currency's own scale and the invoice refuses a
        // line finer than that - see the note in the README. The line stays because it
        // is what makes the journal balance by construction rather than by luck, and
        // this test says which of the two situations the books are actually in.
        Books books = Books.ForVat();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m);

        Voucher journal = books.Raise(invoice).Value;

        Posted(journal, books.AccountFor(StockAccount.RoundOff)).ShouldBe(0m);
        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_journal_balances_whatever_it_carries()
    {
        // The property that matters more than any individual figure: a voucher that does
        // not balance cannot post, so an unbalanced journal is a sale that cannot be
        // made rather than books that are quietly wrong.
        Books books = Books.ForGst();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);
        AdditionalLedger discount = Books.Charge("DISC-ALLOWED", isAddition: false);

        SalesInvoice invoice = books.Sell(
            taxable: 333.33m,
            tax: 59.99m,
            charges: [(freight, 12.35m), (discount, 7.77m)]);

        Voucher journal = books.Raise(invoice).Value;

        Balances(journal).ShouldBeTrue();
        journal.Post(User, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_return_runs_the_same_journal_with_every_side_swapped()
    {
        // Goods coming back credit the customer what they no longer owe, take the goods
        // off revenue, and put the tax back out of the liability. The same arithmetic,
        // which is why it is one piece of code and not two.
        Books books = Books.ForVat();
        SalesInvoice credit = books.Sell(taxable: 1_000m, tax: 50m, kind: SalesDocumentKind.Return);

        Voucher journal = books.Raise(credit).Value;

        Credit(journal, credit.CustomerLedgerId).ShouldBe(1_050m);
        Debit(journal, books.AccountFor(StockAccount.SalesReturn)).ShouldBe(1_000m);
        Debit(journal, books.TaxLedger(TaxComponentType.Vat)).ShouldBe(50m);

        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_return_goes_to_its_own_account_rather_than_back_to_sales()
    {
        // The business's answer of 2026-08-12. Net revenue is the same either way; what
        // differs is whether the gross figure and the returns stay separately
        // answerable, which is what §12.10's Sales Return Report asks for.
        Books books = Books.ForVat();
        SalesInvoice credit = books.Sell(1_000m, 50m, kind: SalesDocumentKind.Return);

        Voucher journal = books.Raise(credit).Value;

        Posted(journal, books.AccountFor(StockAccount.SalesRevenue)).ShouldBe(0m);
        Posted(journal, books.AccountFor(StockAccount.SalesReturn)).ShouldBe(1_000m);
    }

    [Fact]
    public void A_GST_return_takes_each_head_back_out_of_the_liability()
    {
        Books books = Books.ForGst();
        SalesInvoice credit = books.Sell(1_000m, 180m, kind: SalesDocumentKind.Return);

        Voucher journal = books.Raise(credit).Value;

        Debit(journal, books.TaxLedger(TaxComponentType.Cgst)).ShouldBe(90m);
        Debit(journal, books.TaxLedger(TaxComponentType.Sgst)).ShouldBe(90m);
        Credit(journal, credit.CustomerLedgerId).ShouldBe(1_180m);
    }

    [Fact]
    public void A_charge_on_a_return_swaps_with_everything_else()
    {
        // Freight on a credit note is freight the firm is giving back, so it runs the
        // other way too. Anything else would leave the journal unbalanced by twice it.
        Books books = Books.ForVat();
        AdditionalLedger freight = Books.Charge("FREIGHT", isAddition: true);

        SalesInvoice credit = books.Sell(
            1_000m, 50m, charges: [(freight, 30m)], kind: SalesDocumentKind.Return);

        Voucher journal = books.Raise(credit).Value;

        Debit(journal, freight.LedgerId).ShouldBe(30m);
        Credit(journal, credit.CustomerLedgerId).ShouldBe(1_080m);
        Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public void A_firm_that_has_not_chosen_a_returns_account_cannot_post_one()
    {
        Books books = Books.ForVat(assignRevenue: false);
        SalesInvoice credit = books.Sell(1_000m, 50m, kind: SalesDocumentKind.Return);

        books.Raise(credit).Error.Code.ShouldBe("InventoryAccounts.NotConfigured");
    }

    [Fact]
    public void A_head_with_no_account_stops_the_sale_and_names_the_head()
    {
        // The refusal the map exists for. A firm charging a tax it has nowhere to post is
        // a firm whose return will not reconcile, and the first invoice is the cheapest
        // moment for somebody to find that out.
        Books books = Books.ForGst(assignTax: false);
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 180m);

        Result<Voucher> raised = books.Raise(invoice);

        raised.Error.Code.ShouldBe("TaxAccounts.NotConfigured");
        raised.Error.Description.ShouldContain("Cgst");
    }

    [Fact]
    public void A_firm_that_has_not_chosen_a_sales_account_cannot_post_one()
    {
        Books books = Books.ForVat(assignRevenue: false);
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m);

        books.Raise(invoice).Error.Code.ShouldBe("InventoryAccounts.NotConfigured");
    }

    [Fact]
    public void A_draft_owes_the_ledger_nothing()
    {
        Books books = Books.ForVat();
        SalesInvoice draft = books.Draft();

        books.Raise(draft).Error.Code.ShouldBe("SalesJournal.NotPosted");
    }

    [Fact]
    public void A_sale_in_a_currency_the_firm_does_not_keep_its_books_in_is_refused()
    {
        // Refused rather than posted at a rate of one, which would state a hundred
        // dollars as a hundred riyals and reconcile perfectly while being wrong.
        Books books = Books.ForVat();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m, currency: CurrencyCode.Usd);

        books.Raise(invoice).Error.Code.ShouldBe("SalesJournal.CurrencyNotBase");
    }

    [Fact]
    public void A_map_belonging_to_another_firm_cannot_post_this_firm_s_sale()
    {
        Books books = Books.ForVat();
        SalesInvoice invoice = books.Sell(taxable: 1_000m, tax: 50m);

        Result<Voucher> raised = SalesJournal.Raise(
            invoice,
            InventoryAccountMap.Create(Tenant, FirmId.NewId()),
            books.TaxAccounts,
            books.Firm,
            books.Year,
            "JV/2026/0001");

        raised.Error.Code.ShouldBe("SalesJournal.MapNotInFirm");
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

    /// <summary>A firm, its chart, and the two maps that say where a sale posts.</summary>
    /// <remarks>
    /// Assembled once per test rather than shared, so a test that repoints an account or
    /// leaves one unassigned cannot affect another.
    /// </remarks>
    private sealed class Books
    {
        private readonly Dictionary<StockAccount, Ledger> _ledgers = [];
        private readonly Dictionary<TaxComponentType, Ledger> _taxLedgers = [];
        private readonly TaxRegime _regime;

        private Books(TaxRegime regime, bool assignRevenue, bool assignTax)
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

                // Both revenue accounts together: a firm that has not said where sales
                // go has not said where returns come back from either.
                bool isRevenue = account
                    is StockAccount.SalesRevenue or StockAccount.SalesReturn;

                if (!isRevenue || assignRevenue)
                {
                    Accounts.Assign(account, ledger).IsSuccess.ShouldBeTrue();
                }
            }

            foreach (TaxComponentType head in Heads())
            {
                Ledger ledger = LedgerNamed($"{head}-OUTPUT", LedgerKind.Tax);
                _taxLedgers[head] = ledger;

                if (assignTax)
                {
                    TaxAccounts.Assign(head, TaxDirection.Output, ledger)
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

        public static Books ForVat(bool assignRevenue = true, bool assignTax = true) =>
            new(TaxRegime.GccVat, assignRevenue, assignTax);

        public static Books ForGst(bool assignRevenue = true, bool assignTax = true) =>
            new(TaxRegime.IndiaGst, assignRevenue, assignTax);

        public LedgerId AccountFor(StockAccount account) => _ledgers[account].Id;

        public LedgerId TaxLedger(TaxComponentType head) => _taxLedgers[head].Id;

        public Result<Voucher> Raise(SalesInvoice invoice) =>
            SalesJournal.Raise(invoice, Accounts, TaxAccounts, Firm, Year, "JV/2026/0001");

        public SalesInvoice Draft(
            CurrencyCode? currency = null,
            SalesDocumentKind kind = SalesDocumentKind.Invoice) =>
            SalesInvoice.CreateDraft(
                Tenant,
                FirmKey,
                Branch,
                Year,
                "SL/2026/0001",
                Date,
                Customer(),
                Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
                TaxMode.Tax,
                currency ?? Qar,
                kind).Value;

        /// <summary>Raises and posts an invoice for one line assessed as stated.</summary>
        public SalesInvoice Sell(
            decimal taxable,
            decimal tax,
            IReadOnlyList<(AdditionalLedger Charge, decimal Amount)>? charges = null,
            CurrencyCode? currency = null,
            SalesDocumentKind kind = SalesDocumentKind.Invoice)
        {
            SalesInvoice invoice = Draft(currency, kind);
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
                ChargeableDocument.Sales,
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

        private static Ledger Customer() =>
            Ledger.Create(
                AccountGroup.CreateRoot(
                    Tenant, FirmKey, "G1200", "Debtors", AccountNature.Asset).Value,
                "1200",
                "Al Mansoor",
                LedgerKind.Customer,
                Qar).Value;

        private static Ledger LedgerNamed(string code, LedgerKind kind) =>
            Ledger.Create(
                AccountGroup.CreateRoot(
                    Tenant, FirmKey, $"G{code}", $"Group {code}", AccountNature.Income).Value,
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
