using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Tests for stock operations and average costing, end to end.</summary>
/// <remarks>
/// The arithmetic of the weighted average is covered in the domain tests, where it is
/// cheap to cover exhaustively. What these cover is what only shows up once the whole
/// stack is involved: that a receipt actually reaches a position, that a transfer
/// preserves value across two warehouses, that an issue is refused when the goods are
/// not there, and that cancelling a document removes exactly what it added rather than
/// what the goods are worth today.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class StockEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Stock = $"{Inventory}/stock";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public StockEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_receipt_puts_goods_on_hand_at_what_they_cost()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        receipt.Number.ShouldStartWith("MR");
        receipt.Status.ShouldBe(StockDocumentStatus.Posted);
        receipt.Movements.ShouldBe(1);
        receipt.TotalValue.ShouldBe(250m);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(10m);
        row.AverageCost.ShouldBe(25m);
        row.Value.ShouldBe(250m);
    }

    [Fact]
    public async Task A_second_receipt_moves_the_average_by_weight()
    {
        // The whole answer to open question 6, end to end: 10 at 25 and 30 at 35 is
        // 1150 over 40, not the 30 an average of the two prices would give.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 30m, rate: 35m)]);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(40m);
        row.AverageCost.ShouldBe(32.5m);
        row.Value.ShouldBe(1300m);
    }

    [Fact]
    public async Task An_issue_takes_goods_out_at_the_average_and_leaves_it_alone()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 40m, rate: 32.5m)]);

        CreateStockDocumentResponse issue = await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 15m)]);

        issue.Number.ShouldStartWith("MI");
        issue.TotalValue.ShouldBe(487.5m);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(25m);
        row.AverageCost.ShouldBe(32.5m);
    }

    [Fact]
    public async Task An_issue_for_more_than_is_on_hand_is_refused_and_names_the_product()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 5m, rate: 10m)]);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 6m)]);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Naming the product matters on a forty-line document, where "not enough
        // stock" on its own is not something anybody can act on.
        string body = await refused.Content.ReadAsStringAsync();
        body.ShouldContain(fixtures.ProductCode);

        // And nothing moved: the whole document rolls back, not just the bad line.
        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(5m);
    }

    [Fact]
    public async Task A_transfer_moves_goods_between_warehouses_at_the_cost_they_leave_at()
    {
        // A transfer is not a purchase: the firm still owns the same goods at the same
        // cost, so the value has to survive the move intact.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 35m)]);

        CreateStockDocumentResponse transfer = await PostAsync(
            client, StockDocumentType.StockTransfer, fixtures.MainId,
            [Line(fixtures.ProductId, 8m)], destination: fixtures.ShopId);

        transfer.Movements.ShouldBe(2);
        transfer.TotalValue.ShouldBe(240m);

        StockValuationRow main = await PositionAsync(client, fixtures, fixtures.MainId);
        main.Quantity.ShouldBe(12m);
        main.AverageCost.ShouldBe(30m);

        StockValuationRow shop = await PositionAsync(client, fixtures, fixtures.ShopId);
        shop.Quantity.ShouldBe(8m);
        shop.AverageCost.ShouldBe(30m);

        // Nothing was created or destroyed by moving it.
        (main.Value + shop.Value).ShouldBe(600m);
    }

    [Fact]
    public async Task A_transfer_to_the_same_warehouse_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.StockTransfer, fixtures.MainId,
            [Line(fixtures.ProductId, 1m)], destination: fixtures.MainId);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_quantity_entered_in_a_bigger_unit_is_converted_to_stock_units()
    {
        // Four cases of twenty-four is ninety-six pieces, and the rate is per piece.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 4m, unitId: fixtures.BoxId, rate: 5m)]);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(96m);
        row.AverageCost.ShouldBe(5m);
        row.Value.ShouldBe(480m);
    }

    [Fact]
    public async Task A_unit_that_does_not_convert_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, unitId: fixtures.KilogramId, rate: 5m)]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_adjustment_corrects_a_position_in_either_direction()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 20m, rate: 10m)]);

        // Found stock, valued at what the position already says it cost: finding three
        // on a shelf is not buying them, so the firm's cost has not changed.
        await PostAsync(
            client, StockDocumentType.StockAdjustment, fixtures.MainId,
            [Line(fixtures.ProductId, 3m)]);

        StockValuationRow found = await PositionAsync(client, fixtures, fixtures.MainId);
        found.Quantity.ShouldBe(23m);
        found.AverageCost.ShouldBe(10m);

        await PostAsync(
            client, StockDocumentType.StockAdjustment, fixtures.MainId,
            [Line(fixtures.ProductId, -5m)]);

        StockValuationRow lost = await PositionAsync(client, fixtures, fixtures.MainId);
        lost.Quantity.ShouldBe(18m);
        lost.AverageCost.ShouldBe(10m);
    }

    [Fact]
    public async Task A_physical_count_posts_only_the_difference()
    {
        // The line is what was found on the shelf, not what moved. A count that agrees
        // with the system moves nothing at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 100m, rate: 4m)]);

        CreateStockDocumentResponse agrees = await PostAsync(
            client, StockDocumentType.PhysicalVerification, fixtures.MainId,
            [Line(fixtures.ProductId, 100m)]);

        agrees.Movements.ShouldBe(0);

        CreateStockDocumentResponse short_ = await PostAsync(
            client, StockDocumentType.PhysicalVerification, fixtures.MainId,
            [Line(fixtures.ProductId, 94m)]);

        short_.Movements.ShouldBe(1);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(94m);
        row.AverageCost.ShouldBe(4m);
    }

    [Fact]
    public async Task Cancelling_a_receipt_removes_exactly_what_it_added()
    {
        // The case a naive reversal gets wrong. The average has moved to 30 since, so
        // issuing ten would take 300 back out where 250 went in — and the 50 would
        // vanish into the average of what remains.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        CreateStockDocumentResponse first = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 35m)]);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Stock}/documents/{first.StockDocumentId}/cancel",
            new { Reason = "Entered against the wrong godown" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(10m);
        row.AverageCost.ShouldBe(35m);
        row.Value.ShouldBe(350m);

        // Reversed, not deleted: the document and both movements are still there.
        StockDocumentDetail detail = await ReadAsync(client, first.StockDocumentId);
        detail.Status.ShouldBe(StockDocumentStatus.Cancelled);
        detail.CancellationReason.ShouldBe("Entered against the wrong godown");
        detail.Movements.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_receipt_whose_goods_have_gone_cannot_be_cancelled()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 6m)]);

        HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"{Stock}/documents/{receipt.StockDocumentId}/cancel",
            new { Reason = "Wrong supplier" });

        // Un-receiving goods that have left is not something the books can express,
        // and inventing a figure for it would be worse than saying so.
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        StockDocumentDetail detail = await ReadAsync(client, receipt.StockDocumentId);
        detail.Status.ShouldBe(StockDocumentStatus.Posted);
    }

    [Fact]
    public async Task A_draft_moves_nothing_until_it_is_posted()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        CreateStockDocumentResponse draft = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 7m, rate: 12m)], post: false);

        draft.Status.ShouldBe(StockDocumentStatus.Draft);
        draft.Movements.ShouldBe(0);

        (await ValuationAsync(client)).Rows
            .ShouldNotContain(row => row.ProductId == fixtures.ProductId);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Stock}/documents/{draft.StockDocumentId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK);

        StockValuationRow row = await PositionAsync(client, fixtures, fixtures.MainId);
        row.Quantity.ShouldBe(7m);
        row.AverageCost.ShouldBe(12m);
    }

    [Fact]
    public async Task The_stock_ledger_shows_every_movement_with_the_position_it_left()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);
        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 30m, rate: 35m)]);
        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 5m)]);

        StockLedgerReport ledger = (await client.GetFromJsonAsync<StockLedgerReport>(
            $"{Stock}/ledger?productId={fixtures.ProductId}"
            + $"&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))!;

        ledger.ProductCode.ShouldBe(fixtures.ProductCode);
        ledger.Rows.Count.ShouldBe(3);
        ledger.OpeningQuantity.ShouldBe(0m);
        ledger.TotalIn.ShouldBe(40m);
        ledger.TotalOut.ShouldBe(5m);
        ledger.ClosingQuantity.ShouldBe(35m);

        // The running column is the point of the report: it says what the position was
        // after each movement, as the system believed it at the time.
        ledger.Rows[0].BalanceQuantity.ShouldBe(10m);
        ledger.Rows[0].BalanceAverageCost.ShouldBe(25m);
        ledger.Rows[1].BalanceQuantity.ShouldBe(40m);
        ledger.Rows[1].BalanceAverageCost.ShouldBe(32.5m);
        ledger.Rows[2].QuantityOut.ShouldBe(5m);
        ledger.Rows[2].BalanceQuantity.ShouldBe(35m);
    }

    [Fact]
    public async Task Item_movement_reports_what_moved_over_a_period()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 12m, rate: 10m)]);
        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 4m)]);

        IReadOnlyList<ItemMovementRow> movement =
            (await client.GetFromJsonAsync<IReadOnlyList<ItemMovementRow>>(
                $"{Stock}/movement?from={Today.AddDays(-1):yyyy-MM-dd}"
                + $"&to={Today:yyyy-MM-dd}&categoryId={fixtures.CategoryId}"))!;

        ItemMovementRow row = movement.Single(candidate => candidate.ProductId == fixtures.ProductId);
        row.QuantityIn.ShouldBe(12m);
        row.QuantityOut.ShouldBe(4m);
        row.ValueIn.ShouldBe(120m);
        row.ValueOut.ShouldBe(40m);
        row.Movements.ShouldBe(2);
    }

    [Fact]
    public async Task A_service_item_cannot_be_stocked()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        Guid serviceId = await Fixtures.CreateProductAsync(
            client, fixtures, $"SVC{fixtures.Suffix}", "Repair labour", itemType: 2);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(serviceId, 1m, rate: 50m)]);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_rate_on_a_document_that_does_not_carry_one_is_refused()
    {
        // Recording it and ignoring it would be worse: somebody would set it and
        // believe it had priced the issue.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 10m)]);

        HttpResponseMessage refused = await SendAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 1m, rate: 99m)]);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Documents_are_listed_and_read_back_in_full()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Fixtures fixtures = await Fixtures.CreateAsync(client);

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 6m, rate: 15m)],
            reference: $"GRN{fixtures.Suffix}");

        IReadOnlyList<StockDocumentSummary> listed =
            (await client.GetFromJsonAsync<IReadOnlyList<StockDocumentSummary>>(
                $"{Stock}/documents?from={Today.AddDays(-1):yyyy-MM-dd}"
                + $"&to={Today:yyyy-MM-dd}&warehouseId={fixtures.MainId}"))!;

        StockDocumentSummary summary =
            listed.Single(row => row.Id == receipt.StockDocumentId);
        summary.ReferenceNumber.ShouldBe($"GRN{fixtures.Suffix}");
        summary.LineCount.ShouldBe(1);
        summary.TotalQuantity.ShouldBe(6m);
        summary.TotalValue.ShouldBe(90m);

        StockDocumentDetail detail = await ReadAsync(client, receipt.StockDocumentId);
        detail.Lines.Single().ProductCode.ShouldBe(fixtures.ProductCode);
        detail.Lines.Single().Rate.ShouldBe(15m);
        detail.Movements.Single().BalanceQuantity.ShouldBe(6m);
    }

    [Fact]
    public async Task Stock_refuses_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        (await client.GetAsync($"{Stock}/valuation")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync(
            $"{Stock}/documents?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ helpers

    private static object Line(
        Guid productId,
        decimal quantity,
        Guid? unitId = null,
        decimal rate = 0m) =>
        new { ProductId = productId, Quantity = quantity, UnitId = unitId, Rate = rate };

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouseId,
        object[] lines,
        Guid? destination = null,
        string? reference = null,
        bool post = true) =>
        client.PostAsJsonAsync(
            $"{Stock}/documents",
            new
            {
                Type = (int)type,
                Date = Today,
                WarehouseId = warehouseId,
                DestinationWarehouseId = destination,
                Lines = lines,
                ReferenceNumber = reference,
                PostImmediately = post,
            });

    private static async Task<CreateStockDocumentResponse> PostAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouseId,
        object[] lines,
        Guid? destination = null,
        string? reference = null,
        bool post = true)
    {
        HttpResponseMessage response = await SendAsync(
            client, type, warehouseId, lines, destination, reference, post);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!;
    }

    private static async Task<StockDocumentDetail> ReadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<StockDocumentDetail>($"{Stock}/documents/{id}"))!;

    private static async Task<StockValuationReport> ValuationAsync(
        HttpClient client,
        Guid? warehouseId = null) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
            warehouseId is { } id
                ? $"{Stock}/valuation?warehouseId={id}"
                : $"{Stock}/valuation"))!;

    private static async Task<StockValuationRow> PositionAsync(
        HttpClient client,
        Fixtures fixtures,
        Guid warehouseId) =>
        (await ValuationAsync(client, warehouseId)).Rows
            .Single(row => row.ProductId == fixtures.ProductId);

    /// <summary>The masters a stock document needs before it can exist.</summary>
    /// <param name="Suffix">The suffix keeping this test's codes to itself.</param>
    /// <param name="CategoryId">A category.</param>
    /// <param name="EachId">A base unit.</param>
    /// <param name="BoxId">A unit derived from it, so conversion is testable.</param>
    /// <param name="KilogramId">A unit in another group, so refusal is testable.</param>
    /// <param name="MainId">A warehouse.</param>
    /// <param name="ShopId">A second one, so transfers are testable.</param>
    /// <param name="ProductId">A stocked product.</param>
    /// <param name="ProductCode">Its code.</param>
    private sealed record Fixtures(
        string Suffix,
        Guid CategoryId,
        Guid EachId,
        Guid BoxId,
        Guid KilogramId,
        Guid MainId,
        Guid ShopId,
        Guid ProductId,
        string ProductCode)
    {
        internal static async Task<Fixtures> CreateAsync(HttpClient client)
        {
            // Version 4 rather than version 7: a version 7 identifier leads with a
            // millisecond timestamp, so two taken in the same instant share a prefix.
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid categoryId = await CreateMasterAsync(
                client, "categories",
                new { Code = $"CAT{suffix}", Name = $"Stocked {suffix}" });

            Guid eachId = await CreateMasterAsync(
                client, "units", new { Code = $"EA{suffix}", Name = "Each" });

            Guid boxId = await CreateMasterAsync(
                client, "units",
                new
                {
                    Code = $"BX{suffix}",
                    Name = "Box of 24",
                    BaseUnitId = eachId,
                    ConversionFactor = 24m,
                });

            Guid kilogramId = await CreateMasterAsync(
                client, "units", new { Code = $"KG{suffix}", Name = "Kilogram" });

            Guid mainId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"MAIN{suffix}", Name = "Main store" });

            Guid shopId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"SHOP{suffix}", Name = "Shop floor" });

            Fixtures fixtures = new(
                suffix, categoryId, eachId, boxId, kilogramId, mainId, shopId,
                Guid.Empty, string.Empty);

            string code = $"STK{suffix}";
            Guid productId = await CreateProductAsync(client, fixtures, code, "Stocked thing");

            return fixtures with { ProductId = productId, ProductCode = code };
        }

        internal static async Task<Guid> CreateProductAsync(
            HttpClient client,
            Fixtures fixtures,
            string code,
            string description,
            int itemType = 1)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"{Inventory}/products",
                new
                {
                    Code = code,
                    Description = description,
                    CategoryId = fixtures.CategoryId,
                    StockUnitId = fixtures.EachId,
                    ItemType = itemType,
                });

            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            return await response.Content.ReadFromJsonAsync<Guid>();
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
