using ERP.Application.Accounting.Reports;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="TaxReturnReader"/> against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// These figures are filed with a state, so they are checked against a real database
/// rather than a substitute. The two that matter most and are easiest to get wrong: a
/// supply carrying two heads must be counted once in the taxable value and twice in the
/// tax, and a credit note must reduce the period it falls in rather than appear as a
/// second sale.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class TaxReturnReaderTests
{
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly June30 = new(2026, 6, 30);

    private readonly PostgresFixture _fixture;

    public TaxReturnReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_VAT_firm_reports_the_one_head_it_charges()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), taxable: 1_000m, tax: 50m);

        OutputTaxReport report = await books.OutputAsync();

        report.Regime.ShouldBe(TaxRegime.GccVat);
        report.TaxableSupplies.ShouldBe(1_000m);
        report.Totals.ShouldHaveSingleItem().Component.ShouldBe(TaxComponentType.Vat);
        report.Totals[0].TaxAmount.ShouldBe(50m);
        report.Rows.ShouldHaveSingleItem().Number.ShouldBe("SL/0001");
    }

    [Fact]
    public async Task A_supply_carrying_two_heads_is_counted_once_in_the_supplies()
    {
        // The single easiest way to make a GST return wrong, and the hardest to notice:
        // every tax figure would still be right while the sales figure read double.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.IndiaGst);

        await books.SellAsync(
            "SL/0001",
            new DateOnly(2026, 6, 10),
            taxable: 1_000m,
            tax: 180m,
            heads: [TaxComponentType.Cgst, TaxComponentType.Sgst]);

        OutputTaxReport report = await books.OutputAsync();

        report.TaxableSupplies.ShouldBe(1_000m);
        report.Totals.Count.ShouldBe(2);
        report.Totals.Sum(total => total.TaxAmount).ShouldBe(180m);

        // Twice in the rows, because each row states the base its own head was charged
        // on - which is what a per-head listing has to show.
        report.Rows.Count.ShouldBe(2);
        report.Rows.ShouldAllBe(row => row.TaxableAmount == 1_000m);
    }

    [Fact]
    public async Task A_credit_note_reduces_the_period_it_falls_in()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.SellAsync(
            "SR/0001", new DateOnly(2026, 6, 20), 400m, 20m, kind: SalesDocumentKind.Return);

        OutputTaxReport report = await books.OutputAsync();

        report.TaxableSupplies.ShouldBe(600m);
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(30m);

        // And the return is visible as itself rather than folded away.
        report.Rows.Count.ShouldBe(2);
        report.Rows.ShouldContain(row => row.Kind == SalesDocumentKind.Return && row.TaxAmount == -20m);
    }

    [Fact]
    public async Task Only_posted_documents_reach_a_return()
    {
        // A draft has charged nobody anything and a cancelled sale has been taken back.
        // Either would state a liability the firm does not have.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.SellAsync(
            "SL/0002", new DateOnly(2026, 6, 11), 500m, 25m, status: SalesInvoiceStatus.Draft);
        await books.SellAsync(
            "SL/0003", new DateOnly(2026, 6, 12), 700m, 35m, status: SalesInvoiceStatus.Cancelled);

        OutputTaxReport report = await books.OutputAsync();

        report.TaxableSupplies.ShouldBe(1_000m);
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(50m);
    }

    [Fact]
    public async Task Documents_outside_the_period_are_left_out_of_it()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 5, 31), 100m, 5m);
        await books.SellAsync("SL/0002", new DateOnly(2026, 6, 15), 200m, 10m);
        await books.SellAsync("SL/0003", new DateOnly(2026, 7, 1), 400m, 20m);

        OutputTaxReport report = await books.OutputAsync();

        report.TaxableSupplies.ShouldBe(200m);
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(10m);
    }

    [Fact]
    public async Task A_zero_rated_supply_is_reported_apart_from_a_taxed_one()
    {
        // Exports and exempt goods are supplies the firm made and must declare, and a
        // return that folded them into taxable supplies would overstate what is owed on.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.SellAsync("SL/0002", new DateOnly(2026, 6, 11), 300m, 0m);

        OutputTaxReport report = await books.OutputAsync();

        report.TaxableSupplies.ShouldBe(1_000m);
        report.ZeroRatedSupplies.ShouldBe(300m);
    }

    [Fact]
    public async Task Input_tax_is_read_from_the_purchases_that_were_charged_it()
    {
        // What the report gained when purchase documents arrived: a base. A ledger
        // posting knows the money and nothing else, and a return has to state what the
        // tax was charged on.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.BuyAsync(
            "PU/0001", new DateOnly(2026, 6, 5), taxable: 2_000m, tax: 100m,
            supplierInvoiceNumber: "GW-4471");

        InputTaxReport report = await books.InputAsync();

        InputTaxRow row = report.Rows.ShouldHaveSingleItem();

        row.Number.ShouldBe("PU/0001");
        row.Kind.ShouldBe(PurchaseDocumentKind.Invoice);
        row.Component.ShouldBe(TaxComponentType.Vat);
        row.TaxAmount.ShouldBe(100m);
        row.TaxableAmount.ShouldBe(2_000m);

        // Reported against the supplier's own tax invoice, which is what a reclaim is
        // made against.
        row.SupplierInvoiceNumber.ShouldBe("GW-4471");
        row.SupplierName.ShouldBe("Gulf Wholesale");
        row.TaxRegistrationNumber.ShouldBe("VAT-8891");

        report.TaxablePurchases.ShouldBe(2_000m);
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task A_purchase_carrying_two_heads_is_counted_once_in_the_purchases()
    {
        // The same trap the output side has, on the other side of the return. A GST
        // purchase carries CGST and SGST on one base; adding the base per head would
        // report twice the purchases the firm actually made, and every tax figure on the
        // return would still be right.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.IndiaGst);

        await books.BuyAsync(
            "PU/0001", new DateOnly(2026, 6, 5), 1_000m, 180m,
            heads: [TaxComponentType.Cgst, TaxComponentType.Sgst]);

        InputTaxReport report = await books.InputAsync();

        report.Rows.Count.ShouldBe(2);
        report.TaxablePurchases.ShouldBe(1_000m);
        report.Totals.Sum(total => total.TaxAmount).ShouldBe(180m);
    }

    [Fact]
    public async Task A_debit_note_reduces_what_is_reclaimable()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.BuyAsync("PU/0001", new DateOnly(2026, 6, 5), 2_000m, 100m);
        await books.BuyAsync(
            "PR/0001", new DateOnly(2026, 6, 20), 500m, 25m,
            kind: PurchaseDocumentKind.Return);

        InputTaxReport report = await books.InputAsync();

        report.TaxablePurchases.ShouldBe(1_500m);
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(75m);
    }

    [Fact]
    public async Task Only_posted_purchases_reach_a_return()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.BuyAsync(
            "PU/0001", new DateOnly(2026, 6, 5), 1_000m, 50m,
            status: PurchaseInvoiceStatus.Draft);

        await books.BuyAsync(
            "PU/0002", new DateOnly(2026, 6, 6), 1_000m, 50m,
            status: PurchaseInvoiceStatus.Cancelled);

        InputTaxReport report = await books.InputAsync();

        report.Rows.ShouldBeEmpty();
        report.TaxablePurchases.ShouldBe(0m);
    }

    [Fact]
    public async Task Input_tax_booked_by_hand_is_still_listed_and_says_it_has_no_base()
    {
        // The half that could not simply be replaced. A journal into an input account is
        // still input tax in the ledger, and dropping it from the listing would make the
        // return understate what is reclaimable - so it is reported with the base absent
        // rather than filled with a guess.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.BuyAsync("PU/0001", new DateOnly(2026, 6, 5), 2_000m, 100m);
        await books.PostToInputAsync("JV/0001", new DateOnly(2026, 6, 12), 30m);

        InputTaxReport report = await books.InputAsync();

        report.Rows.Count.ShouldBe(2);

        InputTaxRow byHand = report.Rows.Single(row => row.Number == "JV/0001");

        byHand.Kind.ShouldBeNull();
        byHand.TaxableAmount.ShouldBeNull();
        byHand.TaxAmount.ShouldBe(30m);

        // The tax counts; the base is only what the documents account for.
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(130m);
        report.TaxablePurchases.ShouldBe(2_000m);
    }

    [Fact]
    public async Task A_purchase_journal_is_not_counted_twice()
    {
        // The purchase's own journal debits the input account, and the document reports
        // the same tax. Reading both without excluding one would double every reclaim
        // the firm makes.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.BuyAsync("PU/0001", new DateOnly(2026, 6, 5), 2_000m, 100m);
        await books.RaiseJournalForAsync("PU/0001", new DateOnly(2026, 6, 5), 100m);

        InputTaxReport report = await books.InputAsync();

        report.Rows.ShouldHaveSingleItem().Number.ShouldBe("PU/0001");
        report.Totals.ShouldHaveSingleItem().TaxAmount.ShouldBe(100m);

        // And the ledger agrees with the documents, so the summary is content.
        TaxSummaryReport summary = await books.SummaryAsync();

        summary.Lines.ShouldHaveSingleItem().InputTaxPosted.ShouldBe(100m);
        summary.Lines[0].InputDifference.ShouldBe(0m);
    }

    [Fact]
    public async Task The_summary_states_what_is_left_to_pay()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.PostToInputAsync("JV/0001", new DateOnly(2026, 6, 5), 20m);

        TaxSummaryReport report = await books.SummaryAsync();

        TaxSummaryLine line = report.Lines.ShouldHaveSingleItem();

        line.OutputTax.ShouldBe(50m);
        line.InputTax.ShouldBe(20m);
        line.NetPayable.ShouldBe(30m);
        report.NetPayable.ShouldBe(30m);
    }

    [Fact]
    public async Task A_journal_written_straight_into_a_tax_account_is_surfaced()
    {
        // The case a return built from documents alone would silently understate. It is
        // reported rather than corrected, because only a person can say which is right.
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.PostToOutputAsync("JV/0002", new DateOnly(2026, 6, 12), 15m);

        TaxSummaryReport report = await books.SummaryAsync();

        TaxSummaryLine line = report.Lines.ShouldHaveSingleItem();

        line.OutputTax.ShouldBe(50m);
        line.OutputTaxPosted.ShouldBe(15m);
        line.Difference.ShouldBe(-35m);
        report.IsReconciled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_firm_whose_books_agree_reports_itself_reconciled()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);
        await books.PostToOutputAsync("JV/0002", new DateOnly(2026, 6, 12), 50m);

        TaxSummaryReport report = await books.SummaryAsync();

        report.Lines.ShouldHaveSingleItem().Difference.ShouldBe(0m);
        report.IsReconciled.ShouldBeTrue();
    }

    [Fact]
    public async Task One_firm_never_sees_another_firm_s_tax()
    {
        Books books = await Books.CreateAsync(_fixture, TaxRegime.GccVat);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 10), 1_000m, 50m);

        OutputTaxReport elsewhere = await books.OutputAsync(firmId: FirmId.NewId());

        elsewhere.Rows.ShouldBeEmpty();
        elsewhere.TaxableSupplies.ShouldBe(0m);
    }

    /// <summary>A firm with a tax map, something to sell, and somewhere to post.</summary>
    private sealed class Books
    {
        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly Dictionary<TaxComponentType, Ledger> _output = [];
        private readonly Dictionary<TaxComponentType, Ledger> _input = [];

        private Books(PostgresFixture fixture) => _fixture = fixture;

        private FirmId FirmId => Firm.Id;

        private Firm Firm { get; set; } = null!;

        private Ledger Customer { get; set; } = null!;

        private Ledger Supplier { get; set; } = null!;

        private Warehouse Store { get; set; } = null!;

        private Product Product { get; set; } = null!;

        private UnitOfMeasure Unit { get; set; } = null!;

        private FinancialYear Year { get; set; } = null!;

        internal static async Task<Books> CreateAsync(PostgresFixture fixture, TaxRegime regime)
        {
            Books books = new(fixture);

            await using ErpDbContext context = books.CreateContext();

            books.Firm = Firm.Create(
                books._tenantId, $"F{Guid.NewGuid().ToString("N")[..6]}", "Acme",
                CurrencyCode.Qar, regime, "Asia/Qatar").Value;

            if (regime == TaxRegime.IndiaGst)
            {
                books.Firm.SetTaxRegistration("REG-1", "KL").IsSuccess.ShouldBeTrue();
            }

            context.Firms.Add(books.Firm);

            AccountGroup debtors = AccountGroup.CreateRoot(
                books._tenantId, books.FirmId, "SD", "Sundry Debtors",
                AccountNature.Asset).Value;

            context.AccountGroups.Add(debtors);

            books.Customer = Ledger.Create(
                debtors, "2000", "Al Mansoor", LedgerKind.Customer, CurrencyCode.Qar).Value;

            context.Ledgers.Add(books.Customer);

            AccountGroup creditors = AccountGroup.CreateRoot(
                books._tenantId, books.FirmId, "SC", "Sundry Creditors",
                AccountNature.Liability).Value;

            context.AccountGroups.Add(creditors);

            books.Supplier = Ledger.Create(
                creditors, "3000", "Gulf Wholesale", LedgerKind.Supplier,
                CurrencyCode.Qar).Value;

            books.Supplier.SetTaxDetails("VAT-8891", regime == TaxRegime.IndiaGst ? "KL" : null);

            context.Ledgers.Add(books.Supplier);

            TaxAccountMap map = TaxAccountMap.Create(books._tenantId, books.FirmId);

            AccountGroup taxes = AccountGroup.CreateRoot(
                books._tenantId, books.FirmId, "TAX", "Duties and Taxes",
                AccountNature.Liability).Value;

            context.AccountGroups.Add(taxes);

            foreach (TaxComponentType head in HeadsFor(regime))
            {
                Ledger output = Ledger.Create(
                    taxes, $"OUT-{(int)head}", $"Output {head}", LedgerKind.Tax,
                    CurrencyCode.Qar).Value;

                Ledger input = Ledger.Create(
                    taxes, $"IN-{(int)head}", $"Input {head}", LedgerKind.Tax,
                    CurrencyCode.Qar).Value;

                context.Ledgers.AddRange(output, input);

                map.Assign(head, TaxDirection.Output, output).IsSuccess.ShouldBeTrue();
                map.Assign(head, TaxDirection.Input, input).IsSuccess.ShouldBeTrue();

                books._output[head] = output;
                books._input[head] = input;
            }

            context.TaxAccountMaps.Add(map);

            books.Store = Warehouse.Create(
                books._tenantId, books.FirmId, "MAIN", "Main store").Value;

            context.Warehouses.Add(books.Store);

            books.Unit = UnitOfMeasure.CreateBase(
                books._tenantId, books.FirmId, "EACH", "Each").Value;

            context.UnitsOfMeasure.Add(books.Unit);

            Category category = Category.CreateRoot(
                books._tenantId, books.FirmId, "GEN", "General").Value;

            context.Categories.Add(category);

            books.Product = Product.Create(
                category, books.Unit, "PRO-0001", "A thing", ItemType.Stock,
                CurrencyCode.Qar).Value;

            context.Products.Add(books.Product);

            books.Year = FinancialYear.Create(
                books._tenantId, books.FirmId, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            context.FinancialYears.Add(books.Year);

            await context.SaveChangesAsync();

            return books;
        }

        /// <summary>Files a sales document charging the heads named.</summary>
        internal async Task SellAsync(
            string number,
            DateOnly date,
            decimal taxable,
            decimal tax,
            SalesDocumentKind kind = SalesDocumentKind.Invoice,
            SalesInvoiceStatus status = SalesInvoiceStatus.Posted,
            IReadOnlyList<TaxComponentType>? heads = null)
        {
            await using ErpDbContext context = CreateContext();

            SalesInvoice document = SalesInvoice.CreateDraft(
                _tenantId, FirmId, BranchId.NewId(), Year, number, date,
                Customer, Store, TaxMode.Tax, CurrencyCode.Qar, kind).Value;

            document.AddLine(
                Product, Unit, 1m, 1m, taxable,
                Assessed(taxable, tax, heads ?? [TaxComponentType.Vat]))
                .IsSuccess.ShouldBeTrue();

            if (status != SalesInvoiceStatus.Draft)
            {
                document.Post(UserId.NewId(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            }

            if (status == SalesInvoiceStatus.Cancelled)
            {
                document.Cancel("Raised in error").IsSuccess.ShouldBeTrue();
            }

            context.SalesInvoices.Add(document);

            await context.SaveChangesAsync();
        }

        /// <summary>Files a purchase document charging the heads named.</summary>
        internal async Task BuyAsync(
            string number,
            DateOnly date,
            decimal taxable,
            decimal tax,
            PurchaseDocumentKind kind = PurchaseDocumentKind.Invoice,
            PurchaseInvoiceStatus status = PurchaseInvoiceStatus.Posted,
            IReadOnlyList<TaxComponentType>? heads = null,
            string? supplierInvoiceNumber = null)
        {
            await using ErpDbContext context = CreateContext();

            PurchaseInvoice document = PurchaseInvoice.CreateDraft(
                _tenantId, FirmId, BranchId.NewId(), Year, number, date,
                Supplier, Store, TaxMode.Tax, CurrencyCode.Qar, kind).Value;

            document.AddLine(
                Product, Unit, 1m, 1m, taxable,
                Assessed(taxable, tax, heads ?? [TaxComponentType.Vat]))
                .IsSuccess.ShouldBeTrue();

            document.SetSupplierDocument(supplierInvoiceNumber, null, null)
                .IsSuccess.ShouldBeTrue();

            if (status != PurchaseInvoiceStatus.Draft)
            {
                document.Post(UserId.NewId(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            }

            if (status == PurchaseInvoiceStatus.Cancelled)
            {
                document.Cancel("Entered in error").IsSuccess.ShouldBeTrue();
            }

            context.PurchaseInvoices.Add(document);

            await context.SaveChangesAsync();
        }

        /// <summary>Raises the journal a purchase's posting would have raised, and links it.</summary>
        /// <remarks>
        /// What the posting handler does, done by hand so the reader can be shown a
        /// purchase whose tax is in the ledger as well as on the document. The receipt is
        /// a draft rather than a posted one: nothing here reads it, and the row exists
        /// only so the document has something real to point at.
        /// </remarks>
        internal async Task RaiseJournalForAsync(string number, DateOnly date, decimal tax)
        {
            await using ErpDbContext context = CreateContext();

            PurchaseInvoice document = await context.PurchaseInvoices
                .Include(invoice => invoice.Lines)
                .SingleAsync(invoice => invoice.Number == number);

            StockDocument receipt = StockDocument.CreateDraft(
                _tenantId, FirmId, Year, StockDocumentType.PurchaseReceipt,
                $"PR/{number}", date, Store).Value;

            context.StockDocuments.Add(receipt);

            Voucher journal = Voucher.CreateDraft(
                _tenantId, FirmId, BranchId.NewId(), Year, VoucherType.Journal,
                $"JV/{number}", date, CurrencyCode.Qar, CurrencyCode.Qar).Value;

            journal.AddLine(_input[TaxComponentType.Vat].Id, EntrySide.Debit, tax, "Input tax")
                .IsSuccess.ShouldBeTrue();
            journal.AddLine(Supplier.Id, EntrySide.Credit, tax, "Input tax")
                .IsSuccess.ShouldBeTrue();

            journal.Post(UserId.NewId(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();

            context.Vouchers.Add(journal);

            document.RecordPosting(receipt.Id, null, journal.Id).IsSuccess.ShouldBeTrue();

            await context.SaveChangesAsync();
        }

        /// <summary>Posts a journal debiting an input tax account.</summary>
        internal Task PostToInputAsync(string number, DateOnly date, decimal amount) =>
            PostAsync(number, date, _input[TaxComponentType.Vat], EntrySide.Debit, amount);

        /// <summary>Posts a journal crediting an output tax account.</summary>
        internal Task PostToOutputAsync(string number, DateOnly date, decimal amount) =>
            PostAsync(number, date, _output[TaxComponentType.Vat], EntrySide.Credit, amount);

        internal async Task<OutputTaxReport> OutputAsync(FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new TaxReturnReader(context)
                .ReadOutputAsync(firmId ?? FirmId, June1, June30);
        }

        internal async Task<InputTaxReport> InputAsync()
        {
            await using ErpDbContext context = CreateContext();

            return await new TaxReturnReader(context).ReadInputAsync(FirmId, June1, June30);
        }

        internal async Task<TaxSummaryReport> SummaryAsync()
        {
            await using ErpDbContext context = CreateContext();

            return await new TaxReturnReader(context).ReadSummaryAsync(FirmId, June1, June30);
        }

        /// <summary>The heads a regime charges, for seeding the map.</summary>
        private static IReadOnlyList<TaxComponentType> HeadsFor(TaxRegime regime) =>
            regime == TaxRegime.IndiaGst
                ? [TaxComponentType.Cgst, TaxComponentType.Sgst, TaxComponentType.Igst]
                : [TaxComponentType.Vat];

        /// <summary>Splits a tax figure evenly across the heads a test names.</summary>
        private static TaxAssessment Assessed(
            decimal taxable,
            decimal tax,
            IReadOnlyList<TaxComponentType> heads)
        {
            Money baseAmount = Money.Of(taxable, CurrencyCode.Qar);

            if (tax == 0m)
            {
                return TaxCalculator.Calculate(
                    baseAmount,
                    TaxRate.Zero,
                    new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false));
            }

            // Built through the engine so the components are exactly what a real document
            // would carry, then asked of the regime that produces the heads wanted.
            TaxRegime regime = heads.Contains(TaxComponentType.Cgst)
                ? TaxRegime.IndiaGst
                : TaxRegime.GccVat;

            return TaxCalculator.Calculate(
                baseAmount,
                TaxRate.FromTrusted(decimal.Round(tax / taxable * 100m, 6)),
                new TaxContext(regime, DocumentTaxMode.Taxable, false, false));
        }

        private async Task PostAsync(
            string number,
            DateOnly date,
            Ledger taxAccount,
            EntrySide side,
            decimal amount)
        {
            await using ErpDbContext context = CreateContext();

            Voucher voucher = Voucher.CreateDraft(
                _tenantId, FirmId, BranchId.NewId(), Year, VoucherType.Journal, number,
                date, CurrencyCode.Qar, CurrencyCode.Qar).Value;

            voucher.AddLine(taxAccount.Id, side, amount, "Adjustment").IsSuccess.ShouldBeTrue();
            voucher.AddLine(
                Customer.Id,
                side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit,
                amount,
                "Adjustment").IsSuccess.ShouldBeTrue();

            voucher.Post(UserId.NewId(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();

            context.Vouchers.Add(voucher);

            await context.SaveChangesAsync();
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
