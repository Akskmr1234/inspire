using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Stock;
using ERP.Application.Sales;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Tests the sales endpoints of section 12, end to end.</summary>
/// <remarks>
/// The whole counter flow through the real host: goods are received, an invoice is
/// entered, and posting it issues the stock, raises the debt and moves the books. What is
/// checked at the end is the trial balance, because that is where a sale either shows up
/// correctly or does not.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class SalesEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Invoices = "/api/v1/sales/invoices";
    private const string Reports = "/api/v1/accounting/reports";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public SalesEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_invoice_is_entered_as_a_draft_and_read_back_in_full()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Invoices, Invoice(fixtures, quantity: 3m, rate: 100m, taxPercentage: 5m));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        SalesInvoiceResponse draft =
            (await created.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        draft.Status.ShouldBe(Domain.Sales.SalesInvoiceStatus.Draft);
        draft.Taxable.ShouldBe(300m);
        draft.Tax.ShouldBe(15m);
        draft.Total.ShouldBe(315m);

        SalesInvoiceDetail detail =
            (await client.GetFromJsonAsync<SalesInvoiceDetail>(
                $"{Invoices}/{draft.SalesInvoiceId}"))!;

        detail.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(3m);
        detail.Lines[0].Components.ShouldHaveSingleItem().Amount.ShouldBe(15m);

        // A draft has produced nothing yet, and says so.
        detail.StockDocumentId.ShouldBeNull();
        detail.BillId.ShouldBeNull();
        detail.JournalVoucherId.ShouldBeNull();
    }

    [Fact]
    public async Task Posting_an_invoice_moves_the_goods_the_debt_and_the_books()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        decimal salesBefore = await ClosingCreditAsync(client, "SALES");
        decimal cogsBefore = await ClosingDebitAsync(client, "COGS");
        decimal stockBefore = await ClosingDebitAsync(client, "STOCK");

        Guid invoiceId = await EnterAsync(
            client, fixtures, quantity: 3m, rate: 100m, taxPercentage: 5m);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/post", new { });

        posted.StatusCode.ShouldBe(HttpStatusCode.OK);

        PostSalesInvoiceResponse result =
            (await posted.Content.ReadFromJsonAsync<PostSalesInvoiceResponse>())!;

        result.Total.ShouldBe(315m);
        result.StockDocumentNumber.ShouldStartWith("SI");

        // Revenue is the goods, not the total: the tax belongs to the state.
        (await ClosingCreditAsync(client, "SALES")).ShouldBe(salesBefore + 300m);

        // Three at the 25 they were received at, stated once by the issue alone, and
        // the same 75 taken back out of stock.
        (await ClosingDebitAsync(client, "COGS")).ShouldBe(cogsBefore + 75m);
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockBefore - 75m);
    }

    [Fact]
    public async Task A_posted_invoice_names_what_its_posting_produced()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        Guid invoiceId = await EnterAsync(client, fixtures, 2m, 50m, 5m);

        (await client.PostAsJsonAsync($"{Invoices}/{invoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        SalesInvoiceDetail detail =
            (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{invoiceId}"))!;

        detail.Header.Status.ShouldBe(Domain.Sales.SalesInvoiceStatus.Posted);
        detail.StockDocumentId.ShouldNotBeNull();
        detail.BillId.ShouldNotBeNull();
        detail.JournalVoucherId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_sale_of_more_than_the_warehouse_holds_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        Guid invoiceId = await EnterAsync(client, fixtures, quantity: 999m, rate: 10m);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/post", new { });

        // A business rule rather than a malformed request: the invoice is perfectly
        // well formed, and the shelf simply does not hold what it sells.
        posted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // And the draft is still a draft, so it can be corrected rather than reissued.
        SalesInvoiceDetail detail =
            (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{invoiceId}"))!;

        detail.Header.Status.ShouldBe(Domain.Sales.SalesInvoiceStatus.Draft);
    }

    [Fact]
    public async Task Cancelling_a_posted_invoice_puts_the_goods_and_the_books_back()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        decimal salesBefore = await ClosingCreditAsync(client, "SALES");
        decimal stockBefore = await ClosingDebitAsync(client, "STOCK");

        Guid invoiceId = await EnterAsync(client, fixtures, 3m, 100m, 5m);

        (await client.PostAsJsonAsync($"{Invoices}/{invoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/cancel",
            new { Reason = "Entered against the wrong customer" });

        cancelled.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        // Both journals out of the balances, so the accounts stand where they did.
        (await ClosingCreditAsync(client, "SALES")).ShouldBe(salesBefore);
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockBefore);

        SalesInvoiceDetail detail =
            (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{invoiceId}"))!;

        detail.Header.Status.ShouldBe(Domain.Sales.SalesInvoiceStatus.Cancelled);

        // And it still names what it produced, because the documents are all still there.
        detail.StockDocumentId.ShouldNotBeNull();
        detail.BillId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_cancelled_sale_stops_showing_in_what_the_customer_owes()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        Guid invoiceId = await EnterAsync(client, fixtures, 2m, 100m, 5m);

        (await client.PostAsJsonAsync($"{Invoices}/{invoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        decimal owedAfterPosting = await OutstandingAsync(client, fixtures.CustomerId);

        owedAfterPosting.ShouldBe(210m);

        (await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/cancel", new { Reason = "Raised in error" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The debtors figure and the credit position read the same bills, so withdrawing
        // one takes it out of both at once.
        (await OutstandingAsync(client, fixtures.CustomerId)).ShouldBe(0m);
    }

    [Fact]
    public async Task A_draft_cannot_be_cancelled_and_a_reason_is_required()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        Guid invoiceId = await EnterAsync(client, fixtures, 1m, 10m);

        (await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/cancel", new { Reason = "Nothing happened yet" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await client.PostAsJsonAsync(
            $"{Invoices}/{invoiceId}/cancel", new { Reason = string.Empty }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_return_takes_the_goods_back_and_settles_what_was_owed()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        decimal returnsBefore = await ClosingDebitAsync(client, "SALES-RETURN");

        Guid invoiceId = await EnterAsync(client, fixtures, 3m, 100m, 5m);

        (await client.PostAsJsonAsync($"{Invoices}/{invoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OutstandingAsync(client, fixtures.CustomerId)).ShouldBe(315m);

        // The same endpoints, with the kind and the invoice it is against on the body.
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Invoices,
            new
            {
                Date = Today,
                CustomerLedgerId = fixtures.CustomerId,
                WarehouseId = fixtures.WarehouseId,
                Kind = 2,
                ReturnsInvoiceId = invoiceId,
                Lines = new[]
                {
                    new
                    {
                        ProductId = fixtures.ProductId,
                        Quantity = 3m,
                        Rate = 100m,
                        TaxPercentage = 5m,
                    },
                },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        SalesInvoiceResponse credit =
            (await created.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        credit.Number.ShouldStartWith("SR");

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Invoices}/{credit.SalesInvoiceId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        PostSalesInvoiceResponse result =
            (await posted.Content.ReadFromJsonAsync<PostSalesInvoiceResponse>())!;

        // A return raises no bill of its own; it settles the one the sale raised.
        result.BillId.ShouldBeNull();
        result.StockDocumentNumber.ShouldStartWith("SR");

        (await OutstandingAsync(client, fixtures.CustomerId)).ShouldBe(0m);

        // And what came back is reportable apart from what went out.
        (await ClosingDebitAsync(client, "SALES-RETURN")).ShouldBe(returnsBefore + 300m);
    }

    [Fact]
    public async Task An_invoice_with_no_lines_is_refused_before_it_is_numbered()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SalesFixtures fixtures = await ArrangeAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Invoices,
            new
            {
                Date = Today,
                CustomerLedgerId = fixtures.CustomerId,
                WarehouseId = fixtures.WarehouseId,
                Lines = Array.Empty<object>(),
            });

        created.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_invoice_of_a_firm_the_caller_is_not_in_is_not_found()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"{Invoices}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_sales_endpoints_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync($"{Invoices}/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await client.PostAsJsonAsync(Invoices, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ helpers

    private static object Invoice(
        SalesFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage) =>
        new
        {
            Date = Today,
            CustomerLedgerId = fixtures.CustomerId,
            WarehouseId = fixtures.WarehouseId,
            Lines = new[]
            {
                new
                {
                    ProductId = fixtures.ProductId,
                    Quantity = quantity,
                    Rate = rate,
                    TaxPercentage = taxPercentage,
                },
            },
        };

    private static async Task<Guid> EnterAsync(
        HttpClient client,
        SalesFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage = 0m)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Invoices, Invoice(fixtures, quantity, rate, taxPercentage));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;
    }

    /// <summary>What a customer owes, read the way the credit screen reads it.</summary>
    private static async Task<decimal> OutstandingAsync(HttpClient client, Guid customerId)
    {
        CreditStatus status = (await client.GetFromJsonAsync<CreditStatus>(
            $"/api/v1/accounting/ledgers/{customerId}/credit-status"))!;

        return status.Outstanding;
    }

    private static async Task<decimal> ClosingDebitAsync(HttpClient client, string code) =>
        (await RowAsync(client, code))?.ClosingDebit ?? 0m;

    private static async Task<decimal> ClosingCreditAsync(HttpClient client, string code) =>
        (await RowAsync(client, code))?.ClosingCredit ?? 0m;

    private static async Task<TrialBalanceRow?> RowAsync(HttpClient client, string code)
    {
        DateOnly from = new(Today.Year, 1, 1);

        TrialBalanceResponse report = (await client.GetFromJsonAsync<TrialBalanceResponse>(
            $"{Reports}/trial-balance?from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))!;

        return report.Rows.FirstOrDefault(row =>
            string.Equals(row.LedgerCode, code, StringComparison.Ordinal));
    }

    /// <summary>Creates the masters a sale needs, and puts stock on the shelf.</summary>
    private static async Task<SalesFixtures> ArrangeAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateMasterAsync(
            client, "categories", new { Code = $"SCAT{suffix}", Name = $"Sales {suffix}" });

        Guid unitId = await CreateMasterAsync(
            client, "units", new { Code = $"SEA{suffix}", Name = "Each" });

        Guid warehouseId = await CreateMasterAsync(
            client, "warehouses", new { Code = $"SWH{suffix}", Name = "Sales store" });

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new
            {
                Code = $"SLS{suffix}",
                Description = "A thing to sell",
                CategoryId = categoryId,
                StockUnitId = unitId,
                ItemType = 1,
            });

        product.StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        // Ten at twenty-five, so a sale has something to issue and a cost to issue it at.
        HttpResponseMessage received = await client.PostAsJsonAsync(
            $"{Inventory}/stock/documents",
            new
            {
                Type = (int)StockDocumentType.MaterialReceipt,
                Date = Today,
                WarehouseId = warehouseId,
                Lines = new[]
                {
                    new { ProductId = productId, Quantity = 10m, Rate = 25m },
                },
            });

        received.StatusCode.ShouldBe(HttpStatusCode.Created);

        return new SalesFixtures(
            await CustomerEndpointTests.CreateAsync(client), warehouseId, productId);
    }

    private static async Task<Guid> CreateMasterAsync(
        HttpClient client,
        string resource,
        object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private sealed record SalesFixtures(Guid CustomerId, Guid WarehouseId, Guid ProductId);
}
