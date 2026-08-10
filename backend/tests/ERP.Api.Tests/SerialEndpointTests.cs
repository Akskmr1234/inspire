using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Tests for serial-number tracking, end to end.</summary>
/// <remarks>
/// The state machine is covered in the domain tests. What these cover is what only
/// appears once the whole stack is involved: that a receipt writes units down and
/// posting puts them on a shelf, that an issue has to name units that are there, that a
/// sold unit is never offered again, and that cancelling a receipt unwrites what it
/// wrote.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class SerialEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Stock = $"{Inventory}/stock";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public SerialEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_receipt_writes_the_units_down_and_posting_puts_them_on_the_shelf()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 2m, rate: 900m, serials: ["imei-001", "IMEI-002"])]);

        IReadOnlyList<SerialNumberView> units = await SerialsAsync(client, fixtures);

        units.Select(unit => unit.Number).ShouldBe(["IMEI-001", "IMEI-002"], ignoreOrder: true);
        units.ShouldAllBe(unit => unit.Status == SerialStatus.InStock);
        units.ShouldAllBe(unit => unit.UnitCost == 900m);
        units.ShouldAllBe(unit => unit.WarehouseId == fixtures.MainId);
    }

    [Fact]
    public async Task A_line_needs_one_number_for_every_unit_it_moves()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        // Two moving, one named: the other unit would go untracked for ever.
        HttpResponseMessage short_ = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 2m, rate: 900m, serials: ["IMEI-001"])]);

        short_.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Half a handset is not a thing.
        HttpResponseMessage fractional = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 1.5m, rate: 900m, serials: ["IMEI-001", "IMEI-002"])]);

        fractional.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_unit_that_has_gone_out_is_never_offered_again()
    {
        // Section 12.7's promise, end to end.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 2m, rate: 900m, serials: ["IMEI-001", "IMEI-002"])]);

        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, serials: ["IMEI-001"])]);

        IReadOnlyList<SerialNumberView> left = await SerialsAsync(client, fixtures);
        left.ShouldHaveSingleItem().Number.ShouldBe("IMEI-002");

        // Asking for it again is refused rather than quietly sending it twice.
        HttpResponseMessage again = await SendAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, serials: ["IMEI-001"])]);

        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // And it is still on file, gone rather than deleted.
        IReadOnlyList<SerialNumberView> all = await SerialsAsync(
            client, fixtures, includeGone: true);

        all.Single(unit => unit.Number == "IMEI-001").Status.ShouldBe(SerialStatus.Issued);
    }

    [Fact]
    public async Task An_issue_cannot_invent_a_unit()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, rate: 900m, serials: ["IMEI-001"])]);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, serials: ["IMEI-999"])]);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_transfer_carries_the_unit_to_the_other_godown()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 2m, rate: 900m, serials: ["IMEI-001", "IMEI-002"])]);

        await PostAsync(
            client, StockDocumentType.StockTransfer, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, serials: ["IMEI-002"])],
            destination: fixtures.ShopId);

        IReadOnlyList<SerialNumberView> shop = await SerialsAsync(
            client, fixtures, warehouseId: fixtures.ShopId);

        shop.ShouldHaveSingleItem().Number.ShouldBe("IMEI-002");

        IReadOnlyList<SerialNumberView> main = await SerialsAsync(
            client, fixtures, warehouseId: fixtures.MainId);

        main.ShouldHaveSingleItem().Number.ShouldBe("IMEI-001");
    }

    [Fact]
    public async Task Cancelling_a_receipt_takes_its_units_back_off_the_shelf()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, rate: 900m, serials: ["IMEI-001"])]);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Stock}/documents/{receipt.StockDocumentId}/cancel",
            new { Reason = "Received against the wrong godown" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SerialsAsync(client, fixtures)).ShouldBeEmpty();

        // Written down and un-written, not deleted: the trail of a receipt that was
        // posted and reversed survives.
        IReadOnlyList<SerialNumberView> all = await SerialsAsync(
            client, fixtures, includeGone: true);

        all.ShouldHaveSingleItem().Status.ShouldBe(SerialStatus.Recorded);
    }

    [Fact]
    public async Task A_unit_is_found_by_the_number_on_its_case()
    {
        // What a service desk asks, with no idea which product record it belongs to.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client);

        DateOnly warranty = Today.AddYears(1);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [
                Line(
                    fixtures.ProductId, 1m, rate: 900m, serials: [fixtures.Unique],
                    warrantyUntil: warranty),
            ]);

        IReadOnlyList<SerialNumberView> found =
            (await client.GetFromJsonAsync<IReadOnlyList<SerialNumberView>>(
                $"{Stock}/serials/find?number={fixtures.Unique.ToLowerInvariant()}"))!;

        SerialNumberView unit = found.ShouldHaveSingleItem();
        unit.ProductId.ShouldBe(fixtures.ProductId);
        unit.WarrantyUntil.ShouldBe(warranty);
        unit.IsUnderWarranty.ShouldBeTrue();
    }

    [Fact]
    public async Task Units_cannot_be_named_for_a_product_that_is_not_serialised()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        SerialFixtures fixtures = await SerialFixtures.CreateAsync(client, tracked: false);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, rate: 900m, serials: ["IMEI-001"])]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ helpers

    private static object Line(
        Guid productId,
        decimal quantity,
        decimal rate = 0m,
        IReadOnlyList<string>? serials = null,
        DateOnly? warrantyUntil = null) =>
        new
        {
            ProductId = productId,
            Quantity = quantity,
            Rate = rate,
            SerialNumbers = serials,
            WarrantyUntil = warrantyUntil,
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

    private static async Task<IReadOnlyList<SerialNumberView>> SerialsAsync(
        HttpClient client,
        SerialFixtures fixtures,
        Guid? warehouseId = null,
        bool includeGone = false)
    {
        string query = $"{Stock}/serials?productId={fixtures.ProductId}"
            + $"&includeGone={includeGone}";

        if (warehouseId is { } warehouse)
        {
            query += $"&warehouseId={warehouse}";
        }

        return (await client.GetFromJsonAsync<IReadOnlyList<SerialNumberView>>(query))!;
    }

    /// <summary>The masters a serialised stock document needs before it can exist.</summary>
    /// <param name="EachId">A base unit.</param>
    /// <param name="MainId">A warehouse.</param>
    /// <param name="ShopId">A second one, so transfers are testable.</param>
    /// <param name="ProductId">A stocked product, tracked by serial number.</param>
    /// <param name="Unique">A number no other test in this firm will use.</param>
    private sealed record SerialFixtures(
        Guid EachId,
        Guid MainId,
        Guid ShopId,
        Guid ProductId,
        string Unique)
    {
        internal static async Task<SerialFixtures> CreateAsync(
            HttpClient client,
            bool tracked = true)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid categoryId = await CreateMasterAsync(
                client, "categories",
                new { Code = $"SCAT{suffix}", Name = $"Serialised {suffix}" });

            Guid eachId = await CreateMasterAsync(
                client, "units", new { Code = $"SEA{suffix}", Name = "Each" });

            Guid mainId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"SMAIN{suffix}", Name = "Main store" });

            Guid shopId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"SSHOP{suffix}", Name = "Shop floor" });

            HttpResponseMessage created = await client.PostAsJsonAsync(
                $"{Inventory}/products",
                new
                {
                    Code = $"SRL{suffix}",
                    Description = "A handset",
                    CategoryId = categoryId,
                    StockUnitId = eachId,
                    ItemType = 1,
                });

            created.StatusCode.ShouldBe(HttpStatusCode.Created);

            SerialFixtures fixtures = new(
                eachId, mainId, shopId,
                await created.Content.ReadFromJsonAsync<Guid>(),
                $"IMEI-{suffix}");

            if (tracked)
            {
                HttpResponseMessage stocking = await client.PutAsJsonAsync(
                    $"{Inventory}/products/{fixtures.ProductId}/stocking",
                    new
                    {
                        PurchaseUnitId = eachId,
                        SalesUnitId = eachId,
                        TracksSerialNumbers = true,
                    });

                stocking.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            return fixtures;
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
    }
}
