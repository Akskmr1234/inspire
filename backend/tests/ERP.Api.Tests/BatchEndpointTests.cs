using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Tests for batch tracking, end to end.</summary>
/// <remarks>
/// The arithmetic of a batch position is covered in the domain tests. What these cover
/// is what only appears once the whole stack is involved: that a receipt with no batch
/// number generates one, that an issue costs at the batch it was picked from rather
/// than at the product's average, that the two valuations agree afterwards, and that
/// cancelling a batched document puts the goods back into the lot they came out of.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class BatchEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Stock = $"{Inventory}/stock";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public BatchEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_receipt_without_a_batch_number_generates_the_next_one()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m)]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 6m, rate: 6m)]);

        IReadOnlyList<BatchStockRow> batches = await BatchesAsync(client, fixtures);

        // Per product, from A001, exactly as section 10 asks.
        batches.Select(row => row.BatchNumber).ShouldBe(["A001", "A002"], ignoreOrder: true);
        batches.Single(row => row.BatchNumber == "A001").Quantity.ShouldBe(10m);
        batches.Single(row => row.BatchNumber == "A001").PurchaseRate.ShouldBe(5m);
        batches.Single(row => row.BatchNumber == "A002").Quantity.ShouldBe(6m);
        batches.Single(row => row.BatchNumber == "A002").PurchaseRate.ShouldBe(6m);
    }

    [Fact]
    public async Task A_receipt_may_name_the_supplier_lot_and_its_expiry()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        DateOnly expiry = Today.AddDays(180);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 12m, rate: 4m, batchNumber: "pl/2026/0042",
                    manufacturedOn: Today, expiresOn: expiry),
            ]);

        BatchStockRow row = (await BatchesAsync(client, fixtures)).Single();

        row.BatchNumber.ShouldBe("PL/2026/0042");
        row.ExpiresOn.ShouldBe(expiry);
        row.ManufacturedOn.ShouldBe(Today);
        row.DaysToExpiry.ShouldBe(180);
    }

    [Fact]
    public async Task An_issue_costs_at_the_batch_it_was_picked_from()
    {
        // The point of the whole feature. Ten at 5 and ten at 6 average 5.50 across the
        // product; picking from the cheap lot has to take 5 out, not 5.50, and leave
        // the dearer stock behind at 6.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "CHEAP")]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 6m, batchNumber: "DEAR")]);

        StockValuationRow before = await PositionAsync(client, fixtures);
        before.AverageCost.ShouldBe(5.5m);

        CreateStockDocumentResponse issue = await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, batchNumber: "CHEAP")]);

        issue.TotalValue.ShouldBe(50m);

        StockValuationRow after = await PositionAsync(client, fixtures);
        after.Quantity.ShouldBe(10m);
        after.AverageCost.ShouldBe(6m);
        after.Value.ShouldBe(60m);
    }

    [Fact]
    public async Task The_batch_wise_stock_totals_what_the_valuation_totals()
    {
        // Two reports that could disagree about what a shelf is worth would be worse
        // than one report. They cannot, because every batch movement moves the
        // product's position by the same quantity at the same cost.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "L1")]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 7m, rate: 6.5m, batchNumber: "L2")]);
        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 3m, batchNumber: "L2")]);

        StockValuationRow position = await PositionAsync(client, fixtures);

        BatchStockReport report = (await client.GetFromJsonAsync<BatchStockReport>(
            $"{Stock}/batch-stock?productId={fixtures.ProductId}"))!;

        report.Rows.Sum(row => row.Quantity).ShouldBe(position.Quantity);
        report.TotalValue.ShouldBe(position.Value);
        report.Currency.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_batch_cannot_lend_from_another_batch()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "L1")]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "L2")]);

        // Twenty on hand across the product, and only ten in the lot being picked.
        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 15m, batchNumber: "L1")]);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await PositionAsync(client, fixtures)).Quantity.ShouldBe(20m);
    }

    [Fact]
    public async Task An_issue_must_name_a_batch_that_exists()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "L1")]);

        // Nothing named at all: an issue cannot invent the lot it is taking out.
        (await SendAsync(
                client, StockDocumentType.MaterialIssue, fixtures.MainId,
                [Line(fixtures.ProductId, 1m)]))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A lot this product has never had: a typing mistake rather than a new lot.
        (await SendAsync(
                client, StockDocumentType.MaterialIssue, fixtures.MainId,
                [Line(fixtures.ProductId, 1m, batchNumber: "L9")]))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_transfer_carries_the_batch_and_its_cost_to_the_other_godown()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 7m, batchNumber: "L1")]);

        await PostAsync(
            client, StockDocumentType.StockTransfer, fixtures.MainId,
            [Line(fixtures.ProductId, 4m, batchNumber: "L1")],
            destination: fixtures.ShopId);

        BatchStockRow shop = (await BatchesAsync(client, fixtures, fixtures.ShopId)).Single();
        shop.BatchNumber.ShouldBe("L1");
        shop.Quantity.ShouldBe(4m);

        // A transfer is not a purchase: the firm still owns the same goods at the same
        // cost, in a different place.
        shop.UnitCost.ShouldBe(7m);

        BatchStockRow main = (await BatchesAsync(client, fixtures, fixtures.MainId)).Single();
        main.Quantity.ShouldBe(6m);
    }

    [Fact]
    public async Task Cancelling_a_batched_receipt_takes_it_back_out_of_the_batch()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 5m, batchNumber: "L1")]);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Stock}/documents/{receipt.StockDocumentId}/cancel",
            new { Reason = "Received against the wrong godown" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BatchesAsync(client, fixtures)).ShouldBeEmpty();

        // The lot is still on file with nothing in it, which is what the reversal
        // means: the goods went back, the batch was still opened.
        IReadOnlyList<BatchStockRow> emptied = await BatchesAsync(
            client, fixtures, includeEmpty: true);

        emptied.Single().Quantity.ShouldBe(0m);
    }

    [Fact]
    public async Task The_expiry_report_reads_what_is_still_on_a_shelf()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 5m, rate: 5m, batchNumber: "GONE",
                    expiresOn: Today.AddDays(-1)),
            ]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 5m, rate: 5m, batchNumber: "SOON",
                    expiresOn: Today.AddDays(20)),
            ]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 5m, rate: 5m, batchNumber: "LATER",
                    expiresOn: Today.AddDays(400)),
            ]);

        IReadOnlyList<BatchStockRow> expired = await ExpiringAsync(client, fixtures);
        expired.Select(row => row.BatchNumber).ShouldBe(["GONE"]);
        expired.Single().DaysToExpiry.ShouldBe(-1);

        IReadOnlyList<BatchStockRow> soon = await ExpiringAsync(client, fixtures, 30);
        soon.Select(row => row.BatchNumber).ShouldBe(["GONE", "SOON"]);

        // Written off, and off the report with it: a lot nobody holds is not something
        // anybody can act on.
        await PostAsync(
            client, StockDocumentType.DamagedStock, fixtures.MainId,
            [Line(fixtures.ProductId, 5m, batchNumber: "GONE")]);

        (await ExpiringAsync(client, fixtures)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_expiry_date_can_be_corrected_after_the_goods_have_moved()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 5m, rate: 5m, batchNumber: "L1",
                    expiresOn: Today.AddDays(10)),
            ]);

        Guid batchId = (await BatchesAsync(client, fixtures)).Single().BatchId;

        HttpResponseMessage corrected = await client.PutAsJsonAsync(
            $"{Stock}/batches/{batchId}/dates",
            new { ExpiresOn = Today.AddDays(100) });

        corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BatchesAsync(client, fixtures)).Single().ExpiresOn
            .ShouldBe(Today.AddDays(100));

        // A line that quietly restated the date of a lot already on the shelf would be
        // this operation by another name, so the document refuses to be one.
        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 1m, rate: 5m, batchNumber: "L1",
                    expiresOn: Today.AddDays(365)),
            ]);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Batch_tracking_cannot_be_turned_on_over_stock_that_has_no_batch()
    {
        // The position would carry a quantity its batches could not account for, for
        // as long as the product existed, and no later document could reconcile it.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client, tracked: false);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 5m, rate: 5m)]);

        HttpResponseMessage refused = await BatchFixtures.TrackAsync(client, fixtures);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_batch_on_a_product_that_is_not_batched_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client, tracked: false);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 5m, rate: 5m, batchNumber: "L1")]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_document_reads_back_with_the_batch_it_moved()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        BatchFixtures fixtures = await BatchFixtures.CreateAsync(client);

        DateOnly expiry = Today.AddDays(60);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 5m, rate: 5m, batchNumber: "L1", expiresOn: expiry)]);

        StockDocumentDetail detail =
            (await client.GetFromJsonAsync<StockDocumentDetail>(
                $"{Stock}/documents/{receipt.StockDocumentId}"))!;

        detail.Lines.Single().BatchNumber.ShouldBe("L1");
        detail.Lines.Single().ExpiresOn.ShouldBe(expiry);
        detail.Movements.Single().BatchNumber.ShouldBe("L1");
    }

    // ------------------------------------------------------------------ helpers

    private static object Line(
        Guid productId,
        decimal quantity,
        decimal rate = 0m,
        string? batchNumber = null,
        DateOnly? manufacturedOn = null,
        DateOnly? expiresOn = null) =>
        new
        {
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            BatchNumber = batchNumber,
            ManufacturedOn = manufacturedOn,
            ExpiresOn = expiresOn,
        };

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouseId,
        object[] lines,
        Guid? destination = null) =>
        client.PostAsJsonAsync(
            $"{Stock}/documents",
            new
            {
                Type = (int)type,
                Date = Today,
                WarehouseId = warehouseId,
                DestinationWarehouseId = destination,
                Lines = lines,
                PostImmediately = true,
            });

    private static async Task<CreateStockDocumentResponse> PostAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouseId,
        object[] lines,
        Guid? destination = null)
    {
        HttpResponseMessage response = await SendAsync(
            client, type, warehouseId, lines, destination);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!;
    }

    private static async Task<IReadOnlyList<BatchStockRow>> BatchesAsync(
        HttpClient client,
        BatchFixtures fixtures,
        Guid? warehouseId = null,
        bool includeEmpty = false)
    {
        string query = $"{Stock}/batches?productId={fixtures.ProductId}"
            + $"&includeEmpty={includeEmpty}";

        if (warehouseId is { } warehouse)
        {
            query += $"&warehouseId={warehouse}";
        }

        return (await client.GetFromJsonAsync<IReadOnlyList<BatchStockRow>>(query))!;
    }

    private static async Task<IReadOnlyList<BatchStockRow>> ExpiringAsync(
        HttpClient client,
        BatchFixtures fixtures,
        int? withinDays = null)
    {
        string query = $"{Stock}/expiry?asOn={Today:yyyy-MM-dd}"
            + $"&warehouseId={fixtures.MainId}";

        if (withinDays is { } days)
        {
            query += $"&withinDays={days}";
        }

        IReadOnlyList<BatchStockRow> rows =
            (await client.GetFromJsonAsync<IReadOnlyList<BatchStockRow>>(query))!;

        // The report is firm-wide; these tests share a firm, so each one reads back
        // only the product it created.
        return [.. rows.Where(row => row.ProductId == fixtures.ProductId)];
    }

    private static async Task<StockValuationRow> PositionAsync(
        HttpClient client,
        BatchFixtures fixtures) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
                $"{Stock}/valuation?warehouseId={fixtures.MainId}"))!
            .Rows.Single(row => row.ProductId == fixtures.ProductId);

    /// <summary>The masters a batched stock document needs before it can exist.</summary>
    /// <param name="CategoryId">A category.</param>
    /// <param name="EachId">A base unit.</param>
    /// <param name="MainId">A warehouse.</param>
    /// <param name="ShopId">A second one, so transfers are testable.</param>
    /// <param name="ProductId">A stocked product, tracked in batches.</param>
    private sealed record BatchFixtures(
        Guid CategoryId,
        Guid EachId,
        Guid MainId,
        Guid ShopId,
        Guid ProductId)
    {
        internal static async Task<BatchFixtures> CreateAsync(
            HttpClient client,
            bool tracked = true)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid categoryId = await CreateMasterAsync(
                client, "categories",
                new { Code = $"BCAT{suffix}", Name = $"Batched {suffix}" });

            Guid eachId = await CreateMasterAsync(
                client, "units", new { Code = $"BEA{suffix}", Name = "Each" });

            Guid mainId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"BMAIN{suffix}", Name = "Main store" });

            Guid shopId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"BSHOP{suffix}", Name = "Shop floor" });

            HttpResponseMessage created = await client.PostAsJsonAsync(
                $"{Inventory}/products",
                new
                {
                    Code = $"BTC{suffix}",
                    Description = "Batched thing",
                    CategoryId = categoryId,
                    StockUnitId = eachId,
                    ItemType = 1,
                });

            created.StatusCode.ShouldBe(HttpStatusCode.Created);

            BatchFixtures fixtures = new(
                categoryId, eachId, mainId, shopId,
                await created.Content.ReadFromJsonAsync<Guid>());

            if (tracked)
            {
                (await TrackAsync(client, fixtures)).StatusCode
                    .ShouldBe(HttpStatusCode.NoContent);
            }

            return fixtures;
        }

        internal static Task<HttpResponseMessage> TrackAsync(
            HttpClient client,
            BatchFixtures fixtures) =>
            client.PutAsJsonAsync(
                $"{Inventory}/products/{fixtures.ProductId}/stocking",
                new
                {
                    PurchaseUnitId = fixtures.EachId,
                    SalesUnitId = fixtures.EachId,
                    TracksBatches = true,
                });

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
    }
}
