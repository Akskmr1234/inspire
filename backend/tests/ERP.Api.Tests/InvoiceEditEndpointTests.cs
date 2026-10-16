using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>An invoice can be corrected, keeping its number, and a draft can be cancelled.</summary>
/// <remarks>
/// The firm asked for an edit and a cancel on every transaction. These walk the sales side
/// through the real host: a draft rewritten, a posted invoice corrected with the stock and
/// the serial numbers following the correction, and a draft thrown away.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class InvoiceEditEndpointTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public InvoiceEditEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_posted_invoice_is_corrected_under_its_own_number()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client, serialised: true);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await BuyAsync(client, masters, tag, 5m, [$"E-{tag}-1", $"E-{tag}-2", $"E-{tag}-3"]);

        // Sold: two handsets, one named.
        Guid saleId = await SellAsync(client, masters, 2m, [$"E-{tag}-1"]);
        await PostAsync(client, saleId);
        SalesInvoiceDetail sold = await GetAsync(client, saleId);

        (await OnHandAsync(client, masters)).ShouldBe(3m);

        // Corrected: it was three handsets, and the named one was the second.
        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[]
                {
                    new { masters.ProductId, Quantity = 3m, Rate = 1100m, TaxPercentage = 0m, SerialNumbers = new[] { $"E-{tag}-2" } },
                },
                Reason = "Wrong handset scanned",
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        SalesInvoiceDetail after = await GetAsync(client, saleId);
        after.Header.Number.ShouldBe(sold.Header.Number);
        after.Header.Status.ShouldBe(SalesInvoiceStatus.Posted);
        after.Header.Total.ShouldBe(3300m);
        after.StockDocumentId.ShouldNotBe(sold.StockDocumentId);
        after.JournalVoucherId.ShouldNotBe(sold.JournalVoucherId);

        // Five bought, three sold - not two, and not five.
        (await OnHandAsync(client, masters)).ShouldBe(2m);

        // The first handset is back on the shelf; the second has gone.
        List<SerialNumberView> shelf = (await client.GetFromJsonAsync<List<SerialNumberView>>(
            $"/api/v1/inventory/stock/serials?productId={masters.ProductId}&warehouseId={masters.WarehouseId}"))!;

        shelf.Select(unit => unit.Number).ShouldBe([$"E-{tag}-1", $"E-{tag}-3"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_draft_is_rewritten_and_can_be_posted_in_the_same_step()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await BuyAsync(client, masters, tag, 10m, []);
        Guid saleId = await SellAsync(client, masters, 1m, []);
        SalesInvoiceDetail draft = await GetAsync(client, saleId);

        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new object[]
                {
                    new { masters.ProductId, Quantity = 2m, Rate = 50m, TaxPercentage = 0m },
                    new { masters.ProductId, Quantity = 1m, Rate = 40m, TaxPercentage = 0m },
                },
                Narration = "Corrected at the counter",
                Post = true,
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        SalesInvoiceDetail after = await GetAsync(client, saleId);
        after.Header.Number.ShouldBe(draft.Header.Number);
        after.Header.Status.ShouldBe(SalesInvoiceStatus.Posted);
        after.Header.Total.ShouldBe(140m);
        after.Lines.Count.ShouldBe(2);
        after.Narration.ShouldBe("Corrected at the counter");

        (await OnHandAsync(client, masters)).ShouldBe(7m);
    }

    [Fact]
    public async Task A_draft_can_be_cancelled_and_then_cannot_be_edited()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        Guid saleId = await SellAsync(client, masters, 1m, []);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}/cancel", new { Reason = "Customer walked away" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());
        (await GetAsync(client, saleId)).Header.Status.ShouldBe(SalesInvoiceStatus.Cancelled);

        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 10m, TaxPercentage = 0m } },
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_correction_the_stock_cannot_cover_leaves_the_posted_invoice_as_it_was()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await BuyAsync(client, masters, tag, 4m, []);
        Guid saleId = await SellAsync(client, masters, 3m, []);
        await PostAsync(client, saleId);
        SalesInvoiceDetail before = await GetAsync(client, saleId);

        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 9m, Rate = 10m, TaxPercentage = 0m } },
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await edited.Content.ReadAsStringAsync());

        // Nothing half-done: the sale, its issue and the stock are as they were.
        SalesInvoiceDetail after = await GetAsync(client, saleId);
        after.Header.Status.ShouldBe(SalesInvoiceStatus.Posted);
        after.Header.Total.ShouldBe(before.Header.Total);
        after.StockDocumentId.ShouldBe(before.StockDocumentId);
        (await OnHandAsync(client, masters)).ShouldBe(1m);
    }

    [Fact]
    public async Task A_posted_purchase_is_corrected_and_a_cancelled_one_frees_its_supplier_number()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid purchaseId = await BuyAsync(client, masters, tag, 10m, []);
        PurchaseInvoiceDetail bought = await GetPurchaseAsync(client, purchaseId);
        (await OnHandAsync(client, masters)).ShouldBe(10m);

        // The supplier's invoice said twelve, at a better rate.
        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/purchase/invoices/{purchaseId}",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                SupplierInvoiceNumber = $"B-{tag}",
                Lines = new[] { new { masters.ProductId, Quantity = 12m, Rate = 25m, TaxPercentage = 0m } },
                Reason = "Keyed from the delivery note",
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        PurchaseInvoiceDetail after = await GetPurchaseAsync(client, purchaseId);
        after.Header.Number.ShouldBe(bought.Header.Number);
        after.Header.Status.ShouldBe(PurchaseInvoiceStatus.Posted);
        after.Header.Total.ShouldBe(300m);
        (await OnHandAsync(client, masters)).ShouldBe(12m);

        // Cancelled, the supplier's invoice can be entered again from scratch.
        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"/api/v1/purchase/invoices/{purchaseId}/cancel", new { Reason = "Wrong supplier" });
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());
        (await OnHandAsync(client, masters)).ShouldBe(0m);

        await BuyAsync(client, masters, tag, 2m, []);
        (await OnHandAsync(client, masters)).ShouldBe(2m);
    }

    private static async Task<Guid> BuyAsync(
        HttpClient client, TestMasters masters, string tag, decimal quantity, string[] serials)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync(
            "/api/v1/purchase/invoices",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                SupplierInvoiceNumber = $"B-{tag}",
                Lines = new[]
                {
                    new { masters.ProductId, Quantity = quantity, Rate = 30m, TaxPercentage = 0m, SerialNumbers = serials },
                },
            });

        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        return id;
    }

    private static async Task<Guid> SellAsync(
        HttpClient client, TestMasters masters, decimal quantity, string[] serials)
    {
        HttpResponseMessage sold = await client.PostAsJsonAsync(
            "/api/v1/sales/invoices",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                Lines = new[]
                {
                    new { masters.ProductId, Quantity = quantity, Rate = 1000m, TaxPercentage = 0m, SerialNumbers = serials },
                },
            });

        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());

        return (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;
    }

    private static async Task PostAsync(HttpClient client, Guid saleId)
    {
        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/sales/invoices/{saleId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
    }

    private static async Task<PurchaseInvoiceDetail> GetPurchaseAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PurchaseInvoiceDetail>($"/api/v1/purchase/invoices/{id}"))!;

    private static async Task<SalesInvoiceDetail> GetAsync(HttpClient client, Guid saleId) =>
        (await client.GetFromJsonAsync<SalesInvoiceDetail>($"/api/v1/sales/invoices/{saleId}"))!;

    private static async Task<decimal> OnHandAsync(HttpClient client, TestMasters masters)
    {
        StockLedgerReport ledger = (await client.GetFromJsonAsync<StockLedgerReport>(
            $"/api/v1/inventory/stock/ledger?productId={masters.ProductId}&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}&warehouseId={masters.WarehouseId}"))!;

        return ledger.ClosingQuantity;
    }
}
