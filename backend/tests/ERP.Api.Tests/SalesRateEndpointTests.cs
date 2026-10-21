using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>
/// Quotations, the rates the sales screen offers from them and from earlier sales, and
/// profit taken on the cost of the batch actually sold.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SalesRateEndpointTests
{
    private const string Orders = "/api/v1/sales/orders";
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="SalesRateEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public SalesRateEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_quotation_is_kept_apart_from_orders_and_its_rate_is_offered_with_the_last_sale()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        // Nothing quoted or sold yet.
        SalesRateHints none = await HintsAsync(client, masters);
        none.LastQuotation.ShouldBeNull();
        none.LastSale.ShouldBeNull();

        HttpResponseMessage quoted = await client.PostAsJsonAsync(Orders, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = SalesOrderKind.Quotation,
            Lines = new[] { new { masters.ProductId, Quantity = 5m, Rate = 42.5m } },
        });
        quoted.StatusCode.ShouldBe(HttpStatusCode.Created, await quoted.Content.ReadAsStringAsync());
        SalesOrderResponse quotation = (await quoted.Content.ReadFromJsonAsync<SalesOrderResponse>())!;

        quotation.Kind.ShouldBe(SalesOrderKind.Quotation);
        quotation.Number.ShouldStartWith("SQ");

        // On the quotations list, and not on the orders list.
        (await ListAsync(client, masters, SalesOrderKind.Quotation)).ShouldContain(row => row.SalesOrderId == quotation.SalesOrderId);
        (await ListAsync(client, masters, SalesOrderKind.Order)).ShouldNotContain(row => row.SalesOrderId == quotation.SalesOrderId);

        await ReceiveAsync(client, masters.WarehouseId, masters.ProductId, 10m, 30m);
        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 47m, TaxPercentage = 0m } },
            Post = true,
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());

        SalesRateHints hints = await HintsAsync(client, masters);
        hints.LastQuotation!.Rate.ShouldBe(42.5m);
        hints.LastQuotation.Number.ShouldBe(quotation.Number);
        hints.LastSale!.Rate.ShouldBe(47m);
    }

    [Fact]
    public async Task Profit_is_taken_on_the_cost_of_the_batch_sold_and_a_batch_keeps_its_selling_rate()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        HttpResponseMessage tracked = await client.PutAsJsonAsync(
            $"/api/v1/inventory/products/{masters.ProductId}/stocking",
            new { PurchaseUnitId = masters.UnitId, SalesUnitId = masters.UnitId, TracksBatches = true });
        tracked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await tracked.Content.ReadAsStringAsync());

        // I1 bought in batch B1 at 10 and B2 at 15, each priced to sell at 20.
        await BuyAsync(client, masters, "B1", 10m, 20m);
        await BuyAsync(client, masters, "B2", 15m, 20m);

        List<BatchStockRow> batches = (await client.GetFromJsonAsync<List<BatchStockRow>>(
            $"/api/v1/inventory/stock/batches?productId={masters.ProductId}"))!;
        batches.Single(row => row.BatchNumber == "B1").SellingRate.ShouldBe(20m);
        batches.Single(row => row.BatchNumber == "B2").PurchaseRate.ShouldBe(15m);

        // One of each sold at 20.
        foreach (string batch in new[] { "B1", "B2" })
        {
            HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                masters.WarehouseId,
                Kind = 1,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 0m, BatchNumber = batch } },
                Post = true,
            });
            sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        }

        SalesProfitReport report = (await client.GetFromJsonAsync<SalesProfitReport>(
            $"{Invoices}/profit?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&productId={masters.ProductId}"))!;

        SalesProfitRow first = report.Rows.Single(row => row.BatchNumber == "B1");
        first.Sales.ShouldBe(20m);
        first.Cost.ShouldBe(10m);
        first.Profit.ShouldBe(10m);

        SalesProfitRow second = report.Rows.Single(row => row.BatchNumber == "B2");
        second.Cost.ShouldBe(15m);
        second.Profit.ShouldBe(5m);

        report.TotalProfit.ShouldBe(15m);

        // A batch's selling rate can be changed on the batch itself.
        Guid b2 = batches.Single(row => row.BatchNumber == "B2").BatchId;
        (await client.PutAsJsonAsync($"/api/v1/inventory/stock/batches/{b2}/selling-rate", new { SellingRate = 24m }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<BatchStockRow>>(
                $"/api/v1/inventory/stock/batches?productId={masters.ProductId}&includeEmpty=true"))!
            .Single(row => row.BatchNumber == "B2").SellingRate.ShouldBe(24m);
    }

    private static async Task<SalesRateHints> HintsAsync(HttpClient client, TestMasters masters) =>
        (await client.GetFromJsonAsync<SalesRateHints>(
            $"{Invoices}/rates?customerLedgerId={masters.CustomerId}&productId={masters.ProductId}"))!;

    private static async Task<IReadOnlyList<SalesOrderSummary>> ListAsync(
        HttpClient client, TestMasters masters, SalesOrderKind kind) =>
        (await client.GetFromJsonAsync<PagedRows>(
            $"{Orders}?customerLedgerId={masters.CustomerId}&kind={(int)kind}"))!.Items;

    private static async Task ReceiveAsync(
        HttpClient client, Guid warehouseId, Guid productId, decimal quantity, decimal rate)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/inventory/stock/documents", new
        {
            Type = StockDocumentType.MaterialReceipt,
            Date = Today,
            WarehouseId = warehouseId,
            Lines = new[] { new { ProductId = productId, Quantity = quantity, Rate = rate } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task BuyAsync(
        HttpClient client, TestMasters masters, string batch, decimal rate, decimal sellingRate)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"B-{Guid.NewGuid():N}"[..12],
            Lines = new[]
            {
                new { masters.ProductId, Quantity = 1m, Rate = rate, TaxPercentage = 0m, BatchNumber = batch, SellingRate = sellingRate },
            },
        });

        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
    }

    private sealed record PagedRows(List<SalesOrderSummary> Items);
}
