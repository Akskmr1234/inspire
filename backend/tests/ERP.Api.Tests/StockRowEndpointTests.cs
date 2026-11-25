using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Branches;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

/// <summary>
/// The common stock table carries, on every movement, the branch it belongs to and its
/// batch's expiry date - as the legacy Stock table does.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StockRowEndpointTests
{
    private const string Inventory = "/api/v1/inventory";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="StockRowEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public StockRowEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_movement_records_its_branch_and_its_batchs_expiry()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // A branch with a godown of its own.
        HttpResponseMessage opened = await client.PostAsJsonAsync(
            "/api/v1/platform/branches", new SaveBranchRequest($"BR{tag}", $"Branch {tag}", null, null, null, null));
        opened.StatusCode.ShouldBe(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync());
        Guid branch = (await client.GetFromJsonAsync<List<BranchSummary>>("/api/v1/platform/branches"))!
            .Single(row => row.Code == $"BR{tag}").BranchId;

        HttpResponseMessage godown = await client.PostAsJsonAsync(
            $"{Inventory}/warehouses", new { Code = $"GD{tag}", Name = $"Godown {tag}", BranchId = branch });
        godown.StatusCode.ShouldBe(HttpStatusCode.Created, await godown.Content.ReadAsStringAsync());
        Guid branchGodown = await godown.Content.ReadFromJsonAsync<Guid>();

        (await client.PutAsJsonAsync($"{Inventory}/products/{masters.ProductId}/stocking", new
        {
            PurchaseUnitId = masters.UnitId,
            SalesUnitId = masters.UnitId,
            TracksBatches = true,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        DateOnly expiry = Today.AddDays(60);
        CreateStockDocumentResponse intoBranch = await ReceiveAsync(client, branchGodown, masters.ProductId, "L1", expiry);
        CreateStockDocumentResponse intoMain = await ReceiveAsync(client, masters.WarehouseId, masters.ProductId, "L2", expiry);

        // Into the branch's godown: that branch, and the batch's expiry.
        Row branchRow = (await RowsAsync(intoBranch.StockDocumentId)).Single();
        branchRow.BranchId.ShouldBe(branch);
        branchRow.ExpiresOn.ShouldBe(expiry);

        // Into a godown that belongs to no branch: the branch the movement was made in.
        Row mainRow = (await RowsAsync(intoMain.StockDocumentId)).Single();
        mainRow.BranchId.ShouldNotBeNull();
        mainRow.BranchId.ShouldNotBe(branch);

        // A batch's corrected expiry is carried to its movements.
        List<BatchRow> batches = (await client.GetFromJsonAsync<List<BatchRow>>(
            $"{Inventory}/stock/batches?productId={masters.ProductId}"))!;
        Guid lot = batches.First(row => row.BatchNumber == "L1").BatchId;
        (await client.PutAsJsonAsync($"{Inventory}/stock/batches/{lot}/dates", new { ExpiresOn = Today.AddDays(90) }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RowsAsync(intoBranch.StockDocumentId)).Single().ExpiresOn.ShouldBe(Today.AddDays(90));
    }

    private static async Task<CreateStockDocumentResponse> ReceiveAsync(
        HttpClient client, Guid warehouseId, Guid productId, string batch, DateOnly expiresOn)
    {
        HttpResponseMessage received = await client.PostAsJsonAsync($"{Inventory}/stock/documents", new
        {
            Type = (int)StockDocumentType.MaterialReceipt,
            Date = Today,
            WarehouseId = warehouseId,
            Lines = new[] { new { ProductId = productId, Quantity = 5m, Rate = 4m, BatchNumber = batch, ExpiresOn = expiresOn } },
            PostImmediately = true,
        });
        received.StatusCode.ShouldBe(HttpStatusCode.Created, await received.Content.ReadAsStringAsync());

        return (await received.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!;
    }

    private async Task<List<Row>> RowsAsync(Guid documentId)
    {
        List<Row> rows = [];

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            rows = await context.Database
                .SqlQueryRaw<Row>(
                    "SELECT branch_id AS \"BranchId\", expires_on AS \"ExpiresOn\" FROM stock_ledger_entries WHERE document_id = {0}",
                    documentId)
                .ToListAsync());

        rows.ShouldNotBeEmpty();
        return rows;
    }

    /// <summary>A movement's branch and expiry.</summary>
    /// <param name="BranchId">The branch.</param>
    /// <param name="ExpiresOn">The batch's expiry date.</param>
    public sealed record Row(Guid? BranchId, DateOnly? ExpiresOn);

    /// <summary>A batch as the batch list shows it.</summary>
    /// <param name="BatchId">The batch.</param>
    /// <param name="BatchNumber">Its number.</param>
    public sealed record BatchRow(Guid BatchId, string BatchNumber);
}
