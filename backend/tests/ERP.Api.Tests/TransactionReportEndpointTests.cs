using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;

namespace ERP.Api.Tests;

/// <summary>The sales, purchase and profit reports, and the stock reports beside them.</summary>
/// <remarks>
/// Figures a counter can check by hand: ten bought at 6, four sold at 10 with 5% tax and
/// 3 of freight on the bill - so 40 of goods, 2 of tax, 3 of others and 45 in all, and 24
/// of cost leaving 16 of profit.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class TransactionReportEndpointTests
{
    private const string Reports = "/api/v1/reports/transactions";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public TransactionReportEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_sale_reads_by_bill_and_by_item_with_what_it_cost_and_made()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        await BuyAsync(client, masters);
        Guid sale = await SellAsync(client, masters);

        // By bill: the bill's freight is on its row.
        TransactionReport byBill = await ReadAsync(
            client, $"document=Sales&groupBy=Bill&partyLedgerId={masters.CustomerId}");
        TransactionReportRow bill = byBill.Rows.ShouldHaveSingleItem();
        bill.Key.ShouldBe(sale.ToString());
        bill.Quantity.ShouldBe(4m);
        bill.Amount.ShouldBe(40m);
        bill.Tax.ShouldBe(2m);
        bill.Others.ShouldBe(3m);
        bill.Total.ShouldBe(45m);
        bill.Cost.ShouldBe(24m);
        bill.Profit.ShouldBe(16m);
        bill.MarginPercent.ShouldBe(40m);
        byBill.Totals.Total.ShouldBe(45m);

        // By item: the goods and their tax, and none of the bill's freight.
        TransactionReportRow item = (await ReadAsync(
            client, $"document=Sales&groupBy=Item&partyLedgerId={masters.CustomerId}")).Rows.ShouldHaveSingleItem();
        item.Key.ShouldBe(masters.ProductId.ToString());
        item.Others.ShouldBe(0m);
        item.Total.ShouldBe(42m);
        item.Profit.ShouldBe(16m);

        // Narrowed to a tax rate the sale was not charged at, there is nothing.
        (await ReadAsync(client, $"document=Sales&groupBy=Month&partyLedgerId={masters.CustomerId}&taxPercentage=12"))
            .Rows.ShouldBeEmpty();

        // The purchase reads the same way, with no cost of its own.
        TransactionReportRow bought = (await ReadAsync(
            client, $"document=Purchase&groupBy=Party&partyLedgerId={masters.SupplierId}")).Rows.ShouldHaveSingleItem();
        bought.Amount.ShouldBe(60m);
        bought.Quantity.ShouldBe(10m);
        bought.Cost.ShouldBeNull();

        // A salesman is a thing sales have, not purchases.
        (await client.GetAsync($"{Reports}?document=Purchase&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&groupBy=Salesman"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_stock_summary_reads_opening_purchases_sales_and_closing()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        await BuyAsync(client, masters);
        await SellAsync(client, masters);

        StockSummaryRow today = (await client.GetFromJsonAsync<StockSummaryReport>(
            $"/api/v1/inventory/stock/summary?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&productId={masters.ProductId}"))!
            .Rows.ShouldHaveSingleItem();

        today.Opening.ShouldBe(0m);
        today.Purchase.ShouldBe(10m);
        today.Sales.ShouldBe(4m);
        today.OtherIn.ShouldBe(0m);
        today.OtherOut.ShouldBe(0m);
        today.Closing.ShouldBe(6m);

        // The next day opens where today closed, with nothing moving.
        DateOnly tomorrow = Today.AddDays(1);
        StockSummaryRow next = (await client.GetFromJsonAsync<StockSummaryReport>(
            $"/api/v1/inventory/stock/summary?from={tomorrow:yyyy-MM-dd}&to={tomorrow:yyyy-MM-dd}&productId={masters.ProductId}"))!
            .Rows.ShouldHaveSingleItem();

        next.Opening.ShouldBe(6m);
        next.Purchase.ShouldBe(0m);
        next.Closing.ShouldBe(6m);
    }

    [Fact]
    public async Task The_register_and_the_aging_read_what_is_left_and_when_it_came_in()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        // Ten bought some weeks ago at 6, five today at 9, eight sold today: seven left,
        // valued at the average of 7. Newest first, five of them came in today and two
        // weeks ago - or all today, on a first of January with no earlier day this year.
        int gap = Math.Min(40, Today.DayOfYear - 1);
        DateOnly earlier = Today.AddDays(-gap);

        await BuyAsync(client, masters, earlier, 10m, 6m);
        await BuyAsync(client, masters, Today, 5m, 9m);
        await SellAsync(client, masters, 8m);

        StockPositionReport register = (await client.GetFromJsonAsync<StockPositionReport>(
            $"/api/v1/inventory/stock/register?productId={masters.ProductId}"))!;
        StockPositionRow held = register.Rows.ShouldHaveSingleItem();
        held.ProductCode.ShouldBe(masters.ProductCode);
        held.WarehouseId.ShouldBe(masters.WarehouseId);
        held.Quantity.ShouldBe(7m);
        held.Rate.ShouldBe(7m);
        held.Value.ShouldBe(49m);
        register.TotalValue.ShouldBe(49m);

        // As it stood before today's buying and selling: ten, at 6.
        if (gap > 0)
        {
            StockPositionRow before = (await client.GetFromJsonAsync<StockPositionReport>(
                $"/api/v1/inventory/stock/register?productId={masters.ProductId}&asOn={Today.AddDays(-1):yyyy-MM-dd}"))!
                .Rows.ShouldHaveSingleItem();
            before.Quantity.ShouldBe(10m);
            before.Value.ShouldBe(60m);
        }

        StockPositionReport aging = (await client.GetFromJsonAsync<StockPositionReport>(
            $"/api/v1/inventory/stock/aging?productId={masters.ProductId}"))!;
        aging.TotalQuantity.ShouldBe(7m);
        aging.TotalValue.ShouldBe(49m);

        if (gap > 0)
        {
            aging.Rows.Select(row => (row.ReceivedOn, row.AgeDays, row.Quantity, row.Value))
                .ShouldBe([(Today, 0, 5m, 35m), (earlier, gap, 2m, 14m)], ignoreOrder: true);
        }
        else
        {
            aging.Rows.ShouldHaveSingleItem().AgeDays.ShouldBe(0);
        }

        // Narrowed to batches expiring in a range: this product keeps none.
        (await client.GetFromJsonAsync<StockPositionReport>(
            $"/api/v1/inventory/stock/aging?productId={masters.ProductId}&expiresTo={Today.AddYears(1):yyyy-MM-dd}"))!
            .Rows.ShouldBeEmpty();
    }

    private static async Task<TransactionReport> ReadAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync(
            $"{Reports}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<TransactionReport>())!;
    }

    private static Task BuyAsync(HttpClient client, TestMasters masters) =>
        BuyAsync(client, masters, Today, 10m, 6m);

    private static async Task BuyAsync(
        HttpClient client, TestMasters masters, DateOnly date, decimal quantity, decimal rate)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = date,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"R-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = rate, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<Guid> SellAsync(HttpClient client, TestMasters masters) =>
        SellAsync(client, masters, 4m);

    private static async Task<Guid> SellAsync(HttpClient client, TestMasters masters, decimal quantity)
    {
        IReadOnlyList<ChargeSetup> charges = (await client.GetFromJsonAsync<IReadOnlyList<ChargeSetup>>(
            "/api/v1/accounting/charges?document=Sales"))!;
        ChargeSetup freight = charges.Single(charge => charge.Code == "FREIGHT");

        HttpResponseMessage sold = await client.PostAsJsonAsync("/api/v1/sales/invoices", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            Mode = TaxMode.Tax,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 10m, TaxPercentage = 5m } },
            Charges = new[] { new { freight.LedgerId, Amount = 3m } },
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid id = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/sales/invoices/{id}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        return id;
    }
}
