using ERP.Application.Abstractions;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Persistence.Reporting;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="SalesInvoiceReader"/> against a real PostgreSQL instance.
/// </summary>
/// <remarks>
/// Everything worth checking here lives in the query: that paging takes a stable slice,
/// that the filters compose, that the case-insensitive search reaches the index rather
/// than the client, and that a page's totals come out of the documents themselves rather
/// than out of arithmetic written a second time in SQL. A substitute proves none of it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SalesInvoiceReaderTests
{
    private readonly PostgresFixture _fixture;

    public SalesInvoiceReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_page_carries_the_customer_and_the_totals_of_each_document()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 1), quantity: 2m, rate: 100m);

        PagedResult<SalesInvoiceSummary> page = await books.ListAsync(new SalesInvoiceFilter());

        SalesInvoiceSummary row = page.Items.ShouldHaveSingleItem();

        row.Number.ShouldBe("SL/0001");
        row.CustomerName.ShouldBe("Al Mansoor Trading");
        row.Kind.ShouldBe(SalesDocumentKind.Invoice);
        row.LineCount.ShouldBe(1);
        row.Taxable.ShouldBe(200m);
        row.Tax.ShouldBe(10m);
        row.Total.ShouldBe(210m);
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Documents_come_back_newest_first_and_a_page_is_a_stable_slice()
    {
        // The property paging depends on: an order that does not change between one page
        // and the next, or a client would see a row twice and miss another.
        Books books = await Books.CreateAsync(_fixture);

        for (int day = 1; day <= 5; day++)
        {
            await books.SellAsync($"SL/000{day}", new DateOnly(2026, 6, day));
        }

        PagedResult<SalesInvoiceSummary> first = await books.ListAsync(
            new SalesInvoiceFilter(), page: 1, pageSize: 2);

        PagedResult<SalesInvoiceSummary> second = await books.ListAsync(
            new SalesInvoiceFilter(), page: 2, pageSize: 2);

        first.Items.Select(row => row.Number).ShouldBe(["SL/0005", "SL/0004"]);
        second.Items.Select(row => row.Number).ShouldBe(["SL/0003", "SL/0002"]);

        first.TotalCount.ShouldBe(5);
        first.TotalPages.ShouldBe(3);
        first.HasMore.ShouldBeTrue();

        PagedResult<SalesInvoiceSummary> last = await books.ListAsync(
            new SalesInvoiceFilter(), page: 3, pageSize: 2);

        last.Items.ShouldHaveSingleItem().Number.ShouldBe("SL/0001");
        last.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task The_total_counts_what_the_filter_matched_not_what_the_page_holds()
    {
        Books books = await Books.CreateAsync(_fixture);

        for (int day = 1; day <= 4; day++)
        {
            await books.SellAsync($"SL/000{day}", new DateOnly(2026, 6, day));
        }

        PagedResult<SalesInvoiceSummary> page = await books.ListAsync(
            new SalesInvoiceFilter(), page: 1, pageSize: 2);

        page.Items.Count.ShouldBe(2);
        page.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task A_date_range_narrows_the_list_at_both_ends()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 5, 31));
        await books.SellAsync("SL/0002", new DateOnly(2026, 6, 15));
        await books.SellAsync("SL/0003", new DateOnly(2026, 7, 1));

        PagedResult<SalesInvoiceSummary> june = await books.ListAsync(
            new SalesInvoiceFilter(
                From: new DateOnly(2026, 6, 1), To: new DateOnly(2026, 6, 30)));

        june.Items.ShouldHaveSingleItem().Number.ShouldBe("SL/0002");
    }

    [Fact]
    public async Task Returns_sit_among_the_invoices_until_the_kind_narrows_them_out()
    {
        // One list for both, because they are one kind of document: a customer's history
        // wants the credit notes among the sales, not in a second list beside them.
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 1));
        await books.SellAsync("SR/0001", new DateOnly(2026, 6, 2), kind: SalesDocumentKind.Return);

        (await books.ListAsync(new SalesInvoiceFilter())).TotalCount.ShouldBe(2);

        PagedResult<SalesInvoiceSummary> returns = await books.ListAsync(
            new SalesInvoiceFilter(Kind: SalesDocumentKind.Return));

        returns.Items.ShouldHaveSingleItem().Number.ShouldBe("SR/0001");
    }

    [Fact]
    public async Task The_search_matches_a_number_or_a_reference_whatever_the_case()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 1), reference: "PO-ACME-77");
        await books.SellAsync("SL/0002", new DateOnly(2026, 6, 2));

        PagedResult<SalesInvoiceSummary> byReference = await books.ListAsync(
            new SalesInvoiceFilter(Search: "po-acme"));

        byReference.Items.ShouldHaveSingleItem().Number.ShouldBe("SL/0001");

        PagedResult<SalesInvoiceSummary> byNumber = await books.ListAsync(
            new SalesInvoiceFilter(Search: "0002"));

        byNumber.Items.ShouldHaveSingleItem().Number.ShouldBe("SL/0002");
    }

    [Fact]
    public async Task A_filter_matching_nothing_returns_an_empty_page_rather_than_nothing()
    {
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 1));

        PagedResult<SalesInvoiceSummary> page = await books.ListAsync(
            new SalesInvoiceFilter(Search: "nothing matches this"));

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
        page.TotalPages.ShouldBe(0);
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task One_firm_never_sees_another_firm_s_sales()
    {
        // The reader filters by firm as well as leaning on tenant isolation, because a
        // firm is a division within a tenant and nothing in the database separates them.
        Books books = await Books.CreateAsync(_fixture);

        await books.SellAsync("SL/0001", new DateOnly(2026, 6, 1));

        PagedResult<SalesInvoiceSummary> elsewhere = await books.ListAsync(
            new SalesInvoiceFilter(), firmId: FirmId.NewId());

        elsewhere.Items.ShouldBeEmpty();
    }

    /// <summary>A firm with a customer, a product, and somewhere to sell from.</summary>
    private sealed class Books
    {
        private readonly PostgresFixture _fixture;
        private readonly TenantId _tenantId = TenantId.NewId();
        private readonly FirmId _firmId = FirmId.NewId();

        private Books(PostgresFixture fixture) => _fixture = fixture;

        private Ledger Customer { get; set; } = null!;

        private Warehouse Store { get; set; } = null!;

        private Product Product { get; set; } = null!;

        private UnitOfMeasure Unit { get; set; } = null!;

        private FinancialYear Year { get; set; } = null!;

        internal static async Task<Books> CreateAsync(PostgresFixture fixture)
        {
            Books books = new(fixture);

            await using ErpDbContext context = books.CreateContext();

            AccountGroup debtors = AccountGroup.CreateRoot(
                books._tenantId, books._firmId, "SD", "Sundry Debtors",
                AccountNature.Asset).Value;

            context.AccountGroups.Add(debtors);

            books.Customer = Ledger.Create(
                debtors, "2000", "Al Mansoor Trading", LedgerKind.Customer,
                CurrencyCode.Qar).Value;

            context.Ledgers.Add(books.Customer);

            books.Store = Warehouse.Create(
                books._tenantId, books._firmId, "MAIN", "Main store").Value;

            context.Warehouses.Add(books.Store);

            books.Unit = UnitOfMeasure.CreateBase(
                books._tenantId, books._firmId, "EACH", "Each").Value;

            context.UnitsOfMeasure.Add(books.Unit);

            Category category = Category.CreateRoot(
                books._tenantId, books._firmId, "GEN", "General").Value;

            context.Categories.Add(category);

            books.Product = Product.Create(
                category, books.Unit, "PRO-0001", "A thing", ItemType.Stock,
                CurrencyCode.Qar).Value;

            context.Products.Add(books.Product);

            books.Year = FinancialYear.Create(
                books._tenantId, books._firmId, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            context.FinancialYears.Add(books.Year);

            await context.SaveChangesAsync();

            return books;
        }

        /// <summary>Files a sales document with one line on it.</summary>
        internal async Task SellAsync(
            string number,
            DateOnly date,
            decimal quantity = 2m,
            decimal rate = 100m,
            string? reference = null,
            SalesDocumentKind kind = SalesDocumentKind.Invoice)
        {
            await using ErpDbContext context = CreateContext();

            SalesInvoice document = SalesInvoice.CreateDraft(
                _tenantId, _firmId, BranchId.NewId(), Year, number, date,
                Customer, Store, TaxMode.Tax, CurrencyCode.Qar, kind).Value;

            decimal taxable = quantity * rate;

            document.AddLine(
                Product,
                Unit,
                quantity,
                quantity,
                rate,
                TaxCalculator.Calculate(
                    Money.Of(taxable, CurrencyCode.Qar),
                    TaxRate.FromTrusted(5m),
                    new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false)))
                .IsSuccess.ShouldBeTrue();

            document.SetDetails(reference, null).IsSuccess.ShouldBeTrue();

            context.SalesInvoices.Add(document);

            await context.SaveChangesAsync();
        }

        internal async Task<PagedResult<SalesInvoiceSummary>> ListAsync(
            SalesInvoiceFilter filter,
            int page = 1,
            int pageSize = 50,
            FirmId? firmId = null)
        {
            await using ErpDbContext context = CreateContext();

            return await new SalesInvoiceReader(context)
                .ListAsync(firmId ?? _firmId, filter, page, pageSize);
        }

        private ErpDbContext CreateContext() =>
            _fixture.CreateContext(PostgresFixture.ScopedTo(_tenantId));
    }
}
