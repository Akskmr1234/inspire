using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>The history of stock corrections: counts, adjustments and write-offs.</summary>
[Collection(ApiCollection.Name)]
public sealed class StockHistoryEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public StockHistoryEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_count_records_what_was_held_what_was_found_and_the_difference_with_its_reason()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        (Guid warehouse, Guid product) = await ArrangeAsync(client);

        await PostAsync(client, StockDocumentType.MaterialReceipt, warehouse, product, 10m, rate: 20m);
        await PostAsync(
            client, StockDocumentType.PhysicalVerification, warehouse, product, 7m, narration: "Year-end count");

        // A second count that agrees moves nothing, and still belongs in the history.
        await PostAsync(client, StockDocumentType.PhysicalVerification, warehouse, product, 7m);

        await PostAsync(
            client, StockDocumentType.DamagedStock, warehouse, product, 2m, remarks: "Dropped");

        List<StockHistoryRow> counts = await HistoryAsync(client, StockHistoryKind.Adjustments, warehouse);
        counts.Count.ShouldBe(2);

        StockHistoryRow first = counts.Single(row => row.Reason == "Year-end count");
        first.PreviousQuantity.ShouldBe(10m);
        first.ActualQuantity.ShouldBe(7m);
        first.AdjustedQuantity.ShouldBe(-3m);
        first.Value.ShouldBe(-60m);

        StockHistoryRow agreed = counts.Single(row => row.Reason is null);
        agreed.PreviousQuantity.ShouldBe(7m);
        agreed.ActualQuantity.ShouldBe(7m);
        agreed.AdjustedQuantity.ShouldBe(0m);

        StockHistoryRow damaged = (await HistoryAsync(client, StockHistoryKind.Damaged, warehouse)).Single();
        damaged.PreviousQuantity.ShouldBe(7m);
        damaged.ActualQuantity.ShouldBe(5m);
        damaged.AdjustedQuantity.ShouldBe(-2m);
        damaged.Reason.ShouldBe("Dropped");
    }

    private static async Task<List<StockHistoryRow>> HistoryAsync(
        HttpClient client,
        StockHistoryKind kind,
        Guid warehouse) =>
        (await client.GetFromJsonAsync<List<StockHistoryRow>>(
            $"{Inventory}/stock/history?kind={(int)kind}&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&warehouseId={warehouse}"))!;

    private static async Task PostAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouse,
        Guid product,
        decimal quantity,
        decimal rate = 0m,
        string? narration = null,
        string? remarks = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/stock/documents", new
        {
            Type = type,
            Date = Today,
            WarehouseId = warehouse,
            Narration = narration,
            Lines = new[] { new { ProductId = product, Quantity = quantity, Rate = rate, Remarks = remarks } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(Guid Warehouse, Guid Product)> ArrangeAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid category = await CreateAsync(client, "categories", new { Code = $"HC{suffix}", Name = $"History {suffix}" });
        Guid each = await CreateAsync(client, "units", new { Code = $"HE{suffix}", Name = $"Each {suffix}" });
        Guid warehouse = await CreateAsync(client, "warehouses", new { Code = $"HW{suffix}", Name = $"Counted {suffix}" });
        Guid product = await CreateAsync(client, "products", new
        {
            Code = $"HP{suffix}",
            Description = $"Counted thing {suffix}",
            CategoryId = category,
            StockUnitId = each,
            ItemType = 1,
        });

        return (warehouse, product);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
