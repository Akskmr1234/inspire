using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Serial numbers are recorded where a firm has them, and never demanded.</summary>
/// <remarks>
/// The firm's own words: a purchase of five may name five, four, two or none, and a sale
/// may pick the units it is selling from what is on the shelf or name none. These walk
/// the whole path - purchase, post, sell by number, sell without - through the real host.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class OptionalSerialEndpointTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public OptionalSerialEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Some_numbers_in_some_numbers_out_and_the_rest_moves_as_plain_stock()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client, serialised: true);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // Five handsets arrive; two of them have their numbers to hand.
        HttpResponseMessage bought = await client.PostAsJsonAsync(
            "/api/v1/purchase/invoices",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                SupplierInvoiceNumber = $"INV-{tag}",
                Lines = new[]
                {
                    new
                    {
                        masters.ProductId,
                        Quantity = 5m,
                        Rate = 900m,
                        TaxPercentage = 0m,
                        SerialNumbers = new[] { $"IMEI-{tag}-1", $"IMEI-{tag}-2" },
                    },
                },
            });

        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid purchaseId = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;

        HttpResponseMessage received = await client.PostAsJsonAsync(
            $"/api/v1/purchase/invoices/{purchaseId}/post", new { });
        received.StatusCode.ShouldBe(HttpStatusCode.OK, await received.Content.ReadAsStringAsync());

        // Both named units are on the shelf and offered for sale.
        List<SerialNumberView> onShelf = (await client.GetFromJsonAsync<List<SerialNumberView>>(
            $"/api/v1/inventory/stock/serials?productId={masters.ProductId}&warehouseId={masters.WarehouseId}"))!;

        onShelf.Select(unit => unit.Number).ShouldBe(
            [$"IMEI-{tag}-1", $"IMEI-{tag}-2"], ignoreOrder: true);

        // One sold by its number, one sold with none named.
        HttpResponseMessage sold = await client.PostAsJsonAsync(
            "/api/v1/sales/invoices",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                Lines = new object[]
                {
                    new { masters.ProductId, Quantity = 1m, Rate = 1200m, TaxPercentage = 0m, SerialNumbers = new[] { $"IMEI-{tag}-1" } },
                    new { masters.ProductId, Quantity = 1m, Rate = 1200m, TaxPercentage = 0m, SerialNumbers = Array.Empty<string>() },
                },
            });

        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid saleId = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        HttpResponseMessage issued = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{saleId}/post", new { });
        issued.StatusCode.ShouldBe(HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());

        // The unit sold by number has gone; the other named one is still offered.
        List<SerialNumberView> left = (await client.GetFromJsonAsync<List<SerialNumberView>>(
            $"/api/v1/inventory/stock/serials?productId={masters.ProductId}&warehouseId={masters.WarehouseId}"))!;

        left.Select(unit => unit.Number).ShouldBe([$"IMEI-{tag}-2"]);
        left.Single().Status.ShouldBe(SerialStatus.InStock);

        // And a scan of a number finds the product it belongs to.
        List<SerialNumberView> found = (await client.GetFromJsonAsync<List<SerialNumberView>>(
            $"/api/v1/inventory/stock/serials/find?number=IMEI-{tag}-2"))!;

        found.Single().ProductId.ShouldBe(masters.ProductId);
    }
}
