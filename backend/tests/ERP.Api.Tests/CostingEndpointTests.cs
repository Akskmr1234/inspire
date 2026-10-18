using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Products;
using ERP.Application.Purchase;

namespace ERP.Api.Tests;

/// <summary>A product costed at its last purchase rate takes each purchase's.</summary>
[Collection(ApiCollection.Name)]
public sealed class CostingEndpointTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="CostingEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public CostingEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_purchase_sets_the_cost_of_a_product_costed_at_its_last_rate()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        // New products are costed at the last purchase rate unless told otherwise.
        (await CostAsync(client, masters)).ShouldBe(0m);

        await BuyAsync(client, masters, quantity: 4m, rate: 30m, discount: 0m);
        (await CostAsync(client, masters)).ShouldBe(30m);

        // Net of the line's discount, per stock unit: 5 at 22 less 10 is 20 each.
        await BuyAsync(client, masters, quantity: 5m, rate: 22m, discount: 10m);
        (await CostAsync(client, masters)).ShouldBe(20m);
    }

    [Fact]
    public async Task A_product_costed_at_its_average_keeps_the_cost_it_was_given()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        (await client.PutAsJsonAsync(
            $"/api/v1/inventory/products/{masters.ProductId}/rates",
            new { CostingMethod = 2, Cost = 12m, RetailRate = 40m }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await BuyAsync(client, masters, quantity: 2m, rate: 30m, discount: 0m);

        (await CostAsync(client, masters)).ShouldBe(12m);
    }

    private static async Task<decimal> CostAsync(HttpClient client, TestMasters masters) =>
        (await client.GetFromJsonAsync<ProductDetail>(
            $"/api/v1/inventory/products/{masters.ProductId}"))!.Cost;

    private static async Task BuyAsync(
        HttpClient client, TestMasters masters, decimal quantity, decimal rate, decimal discount)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync(
            "/api/v1/purchase/invoices",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                SupplierInvoiceNumber = $"C-{Guid.NewGuid():N}"[..12],
                Lines = new[]
                {
                    new { masters.ProductId, Quantity = quantity, Rate = rate, Discount = discount, TaxPercentage = 0m },
                },
            });

        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
    }
}
