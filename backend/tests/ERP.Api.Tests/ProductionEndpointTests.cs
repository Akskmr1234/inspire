using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Production;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Bills of materials and production runs, end to end.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductionEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Boms = $"{Inventory}/boms";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public ProductionEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_production_run_uses_its_materials_and_makes_the_goods_at_exactly_what_they_cost()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Kit kit = await Kit.ArrangeAsync(client);

        await MoveAsync(client, StockDocumentType.MaterialReceipt, kit, [(kit.Flour, 10m, 5m), (kit.Sugar, 4m, 20m)]);

        // Four cakes: six of flour at 5 and two of sugar at 20, so the cakes are worth 70.
        CreateStockDocumentResponse made = await MoveAsync(
            client, StockDocumentType.Production, kit, [(kit.Flour, -6m, 0m), (kit.Sugar, -2m, 0m), (kit.Cake, 4m, 0m)]);
        made.Number.ShouldStartWith("PRD");

        (await PositionAsync(client, kit, kit.Flour)).ShouldBe((4m, 20m));
        (await PositionAsync(client, kit, kit.Sugar)).ShouldBe((2m, 40m));
        (await PositionAsync(client, kit, kit.Cake)).ShouldBe((4m, 70m));

        // Cancelled, everything goes back where it was.
        (await client.PostAsJsonAsync(
            $"{Inventory}/stock/documents/{made.StockDocumentId}/cancel", new { Reason = "Wrong batch size" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PositionAsync(client, kit, kit.Flour)).ShouldBe((10m, 50m));
        (await PositionAsync(client, kit, kit.Cake)).ShouldBe((0m, 0m));
    }

    [Fact]
    public async Task A_production_run_needs_both_what_it_used_and_what_it_made()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Kit kit = await Kit.ArrangeAsync(client);
        await MoveAsync(client, StockDocumentType.MaterialReceipt, kit, [(kit.Flour, 10m, 5m)]);

        HttpResponseMessage usedOnly = await SendAsync(
            client, StockDocumentType.Production, kit, [(kit.Flour, -2m, 0m)]);
        usedOnly.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await usedOnly.Content.ReadAsStringAsync()).ShouldContain("StockDocument.ProductionIncomplete");

        HttpResponseMessage madeOnly = await SendAsync(
            client, StockDocumentType.Production, kit, [(kit.Cake, 2m, 0m)]);
        madeOnly.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_bill_of_materials_lists_its_components_with_what_they_cost_and_is_one_per_product()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Kit kit = await Kit.ArrangeAsync(client);
        await MoveAsync(client, StockDocumentType.MaterialReceipt, kit, [(kit.Flour, 10m, 5m), (kit.Sugar, 4m, 20m)]);

        object bill = new
        {
            ProductId = kit.Cake,
            OutputQuantity = 2m,
            Components = new[]
            {
                new { ProductId = kit.Flour, Quantity = 3m },
                new { ProductId = kit.Sugar, Quantity = 1m },
            },
        };

        HttpResponseMessage created = await client.PostAsJsonAsync(Boms, bill);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        BillOfMaterialsDetail detail = (await client.GetFromJsonAsync<BillOfMaterialsDetail>($"{Boms}/{id}"))!;
        detail.Summary.OutputQuantity.ShouldBe(2m);
        detail.Components.Count.ShouldBe(2);
        detail.Summary.EstimatedCost.ShouldBe(35m);

        // One bill per finished good.
        (await client.PostAsJsonAsync(Boms, bill)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Not made of itself.
        HttpResponseMessage itself = await client.PutAsJsonAsync($"{Boms}/{id}", new
        {
            ProductId = kit.Cake,
            OutputQuantity = 1m,
            Components = new[] { new { ProductId = kit.Cake, Quantity = 1m } },
        });
        itself.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.GetFromJsonAsync<List<BillOfMaterialsSummary>>(Boms))!
            .ShouldContain(row => row.Id == id && row.ComponentCount == 2);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        StockDocumentType type,
        Kit kit,
        (Guid Product, decimal Quantity, decimal Rate)[] lines) =>
        client.PostAsJsonAsync($"{Inventory}/stock/documents", new
        {
            Type = type,
            Date = Today,
            WarehouseId = kit.WarehouseId,
            Lines = lines.Select(line => new { ProductId = line.Product, line.Quantity, line.Rate }).ToArray(),
        });

    private static async Task<CreateStockDocumentResponse> MoveAsync(
        HttpClient client,
        StockDocumentType type,
        Kit kit,
        (Guid Product, decimal Quantity, decimal Rate)[] lines)
    {
        HttpResponseMessage response = await SendAsync(client, type, kit, lines);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!;
    }

    private static async Task<(decimal Quantity, decimal Value)> PositionAsync(HttpClient client, Kit kit, Guid product)
    {
        StockValuationRow? row = (await client.GetFromJsonAsync<StockValuationReport>(
                $"{Inventory}/stock/valuation?warehouseId={kit.WarehouseId}&includeZero=true"))!
            .Rows.SingleOrDefault(candidate => candidate.ProductId == product);

        return row is null ? (0m, 0m) : (row.Quantity, row.Value);
    }

    /// <summary>A kitchen: two materials, the cake they make, and a store for them.</summary>
    private sealed record Kit(Guid WarehouseId, Guid Flour, Guid Sugar, Guid Cake)
    {
        internal static async Task<Kit> ArrangeAsync(HttpClient client)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid category = await CreateAsync(client, "categories", new { Code = $"KC{suffix}", Name = $"Kitchen {suffix}" });
            Guid each = await CreateAsync(client, "units", new { Code = $"KE{suffix}", Name = "Each" });
            Guid warehouse = await CreateAsync(client, "warehouses", new { Code = $"KW{suffix}", Name = $"Kitchen {suffix}" });

            Task<Guid> Product(string code) => CreateAsync(client, "products", new
            {
                Code = $"{code}{suffix}",
                Description = code,
                CategoryId = category,
                StockUnitId = each,
                ItemType = 1,
            });

            return new Kit(warehouse, await Product("FLR"), await Product("SGR"), await Product("CKE"));
        }

        private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

            return await response.Content.ReadFromJsonAsync<Guid>();
        }
    }
}
