using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Masters;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Settings;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>
/// Transfers between branches, end to end: asked for, agreed, sent into transit, received.
/// </summary>
/// <remarks>
/// What only the whole stack shows: that the goods really leave one warehouse on dispatch,
/// sit in transit at their cost while they travel, and arrive at that cost; that each
/// cancellation takes the request back exactly one step; and that a firm which makes
/// requests compulsory gets no direct transfers.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class StockTransferEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Transfers = $"{Inventory}/stock-transfers";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public StockTransferEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Goods_asked_for_are_approved_sent_through_transit_and_received_at_their_cost()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Masters masters = await Masters.CreateAsync(client);
        await ReceiveAsync(client, masters.MainId, masters.ProductId, 10m, 20m);

        StockTransferResponse raised = await RaiseAsync(client, masters, 6m);
        raised.Number.ShouldStartWith("STR");
        raised.Status.ShouldBe(StockTransferStatus.Requested);

        // The source can spare four of the six.
        HttpResponseMessage approved = await client.PostAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}/approve",
            new { Lines = new[] { new { masters.ProductId, Quantity = 4m } } });
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        StockTransferDetail detail = await DetailAsync(client, raised.StockTransferRequestId);
        detail.Lines[0].ApprovedQuantity.ShouldBe(4m);
        detail.Lines[0].AvailableAtSource.ShouldBe(10m);

        // More than was agreed cannot go.
        HttpResponseMessage tooMany = await DispatchAsync(client, raised, masters.ProductId, 5m);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await tooMany.Content.ReadAsStringAsync());

        HttpResponseMessage sent = await DispatchAsync(client, raised, masters.ProductId, 4m);
        sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await sent.Content.ReadFromJsonAsync<StockTransferResponse>())!.Status
            .ShouldBe(StockTransferStatus.Dispatched);

        Guid transit = await TransitAsync(client);

        (await QuantityAsync(client, masters.MainId, masters.ProductId)).ShouldBe((6m, 120m));
        (await QuantityAsync(client, transit, masters.ProductId)).ShouldBe((4m, 80m));
        (await QuantityAsync(client, masters.ShopId, masters.ProductId)).ShouldBe((0m, 0m));

        HttpResponseMessage received = await client.PostAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}/receive", new { Date = Today });
        received.StatusCode.ShouldBe(HttpStatusCode.OK, await received.Content.ReadAsStringAsync());

        (await QuantityAsync(client, transit, masters.ProductId)).ShouldBe((0m, 0m));
        (await QuantityAsync(client, masters.ShopId, masters.ProductId)).ShouldBe((4m, 80m));

        StockTransferSummary listed = (await client.GetFromJsonAsync<List<StockTransferSummary>>(
            $"{Transfers}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&warehouseId={masters.ShopId}"))!
            .Single(row => row.Id == raised.StockTransferRequestId);

        listed.Status.ShouldBe(StockTransferStatus.Received);
        listed.DispatchedQuantity.ShouldBe(4m);
        listed.DispatchNumber.ShouldNotBeNull();
        listed.ReceiptNumber.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancelling_a_receipt_then_a_dispatch_takes_the_request_back_one_step_at_a_time()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Masters masters = await Masters.CreateAsync(client);
        await ReceiveAsync(client, masters.MainId, masters.ProductId, 10m, 20m);

        StockTransferResponse raised = await RaiseAsync(client, masters, 3m);
        (await client.PostAsJsonAsync($"{Transfers}/{raised.StockTransferRequestId}/approve", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DispatchAsync(client, raised, masters.ProductId, 3m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"{Transfers}/{raised.StockTransferRequestId}/receive", new { Date = Today }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        StockTransferDetail detail = await DetailAsync(client, raised.StockTransferRequestId);

        // The dispatch cannot go while its goods sit in the destination.
        HttpResponseMessage early = await CancelDocumentAsync(client, detail.DispatchDocumentId!.Value);
        early.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await early.Content.ReadAsStringAsync());

        (await CancelDocumentAsync(client, detail.ReceiptDocumentId!.Value)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(client, raised.StockTransferRequestId)).Summary.Status
            .ShouldBe(StockTransferStatus.Dispatched);
        (await QuantityAsync(client, masters.ShopId, masters.ProductId)).Quantity.ShouldBe(0m);

        (await CancelDocumentAsync(client, detail.DispatchDocumentId.Value)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(client, raised.StockTransferRequestId)).Summary.Status
            .ShouldBe(StockTransferStatus.Approved);
        (await QuantityAsync(client, masters.MainId, masters.ProductId)).ShouldBe((10m, 200m));
    }

    [Fact]
    public async Task A_request_is_edited_only_until_it_is_decided_and_rejected_only_with_a_reason()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Masters masters = await Masters.CreateAsync(client);

        StockTransferResponse raised = await RaiseAsync(client, masters, 2m);

        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}", Request(masters, 5m));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        (await DetailAsync(client, raised.StockTransferRequestId)).Lines[0].StockQuantity.ShouldBe(5m);

        (await client.PostAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}/reject", new { Reason = " " }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}/reject", new { Reason = "Needed here" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage late = await client.PutAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}", Request(masters, 1m));
        late.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        StockTransferDetail rejected = await DetailAsync(client, raised.StockTransferRequestId);
        rejected.Summary.Status.ShouldBe(StockTransferStatus.Rejected);
        rejected.Reason.ShouldBe("Needed here");
    }

    [Fact]
    public async Task Where_the_firm_requires_requests_a_direct_transfer_is_refused_and_transit_is_never_entered_by_hand()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        Masters masters = await Masters.CreateAsync(client);
        await ReceiveAsync(client, masters.MainId, masters.ProductId, 5m, 10m);

        // Allowed by default.
        (await DirectAsync(client, masters.MainId, masters.ShopId, masters.ProductId)).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        try
        {
            (await client.PutAsJsonAsync(
                "/api/v1/settings/policies", new FirmPolicies(TransfersRequireApproval: true)))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            HttpResponseMessage refused = await DirectAsync(
                client, masters.MainId, masters.ShopId, masters.ProductId);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("StockTransfer.RequestRequired");
        }
        finally
        {
            (await client.PutAsJsonAsync("/api/v1/settings/policies", FirmPolicies.Default))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Make sure the firm has a transit location, then try to move goods into it by hand.
        StockTransferResponse raised = await RaiseAsync(client, masters, 1m);
        (await client.PostAsJsonAsync($"{Transfers}/{raised.StockTransferRequestId}/approve", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DispatchAsync(client, raised, masters.ProductId, 1m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage byHand = await DirectAsync(
            client, masters.MainId, await TransitAsync(client), masters.ProductId);
        byHand.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await byHand.Content.ReadAsStringAsync()).ShouldContain("StockDocument.TransitByRequestOnly");
    }

    private static object Request(Masters masters, decimal quantity) => new
    {
        Date = Today,
        FromWarehouseId = masters.MainId,
        ToWarehouseId = masters.ShopId,
        Lines = new[] { new { masters.ProductId, Quantity = quantity } },
        Narration = "Running low",
    };

    private static async Task<StockTransferResponse> RaiseAsync(
        HttpClient client,
        Masters masters,
        decimal quantity)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(Transfers, Request(masters, quantity));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<StockTransferResponse>())!;
    }

    private static Task<HttpResponseMessage> DispatchAsync(
        HttpClient client,
        StockTransferResponse raised,
        Guid productId,
        decimal quantity) =>
        client.PostAsJsonAsync(
            $"{Transfers}/{raised.StockTransferRequestId}/dispatch",
            new { Date = Today, Lines = new[] { new { ProductId = productId, Quantity = quantity } } });

    private static Task<HttpResponseMessage> DirectAsync(
        HttpClient client,
        Guid from,
        Guid to,
        Guid productId) =>
        client.PostAsJsonAsync($"{Inventory}/stock/documents", new
        {
            Type = StockDocumentType.StockTransfer,
            Date = Today,
            WarehouseId = from,
            DestinationWarehouseId = to,
            Lines = new[] { new { ProductId = productId, Quantity = 1m } },
        });

    private static Task<HttpResponseMessage> CancelDocumentAsync(HttpClient client, Guid documentId) =>
        client.PostAsJsonAsync(
            $"{Inventory}/stock/documents/{documentId}/cancel", new { Reason = "Entered in error" });

    private static async Task<StockTransferDetail> DetailAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<StockTransferDetail>($"{Transfers}/{id}"))!;

    private static async Task<Guid> TransitAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<WarehouseSummary>>($"{Inventory}/warehouses"))!
            .Single(warehouse => warehouse.IsTransit).Id;

    private static async Task<(decimal Quantity, decimal Value)> QuantityAsync(
        HttpClient client,
        Guid warehouseId,
        Guid productId)
    {
        StockValuationReport report = (await client.GetFromJsonAsync<StockValuationReport>(
            $"{Inventory}/stock/valuation?warehouseId={warehouseId}&includeZero=true"))!;

        StockValuationRow? row = report.Rows.SingleOrDefault(candidate => candidate.ProductId == productId);

        return row is null ? (0m, 0m) : (row.Quantity, row.Value);
    }

    private static async Task ReceiveAsync(
        HttpClient client,
        Guid warehouseId,
        Guid productId,
        decimal quantity,
        decimal rate)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/stock/documents", new
        {
            Type = StockDocumentType.MaterialReceipt,
            Date = Today,
            WarehouseId = warehouseId,
            Lines = new[] { new { ProductId = productId, Quantity = quantity, Rate = rate } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Two warehouses and a product of this test's own.</summary>
    private sealed record Masters(Guid MainId, Guid ShopId, Guid ProductId)
    {
        internal static async Task<Masters> CreateAsync(HttpClient client)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid category = await CreateAsync(
                client, "categories", new { Code = $"TRC{suffix}", Name = $"Transfers {suffix}" });
            Guid each = await CreateAsync(client, "units", new { Code = $"TRE{suffix}", Name = $"Each {suffix}" });
            Guid main = await CreateAsync(
                client, "warehouses", new { Code = $"TRM{suffix}", Name = $"Main {suffix}" });
            Guid shop = await CreateAsync(
                client, "warehouses", new { Code = $"TRS{suffix}", Name = $"Shop {suffix}" });

            HttpResponseMessage product = await client.PostAsJsonAsync($"{Inventory}/products", new
            {
                Code = $"TRP{suffix}",
                Description = $"Travelling thing {suffix}",
                CategoryId = category,
                StockUnitId = each,
                ItemType = 1,
            });
            product.StatusCode.ShouldBe(HttpStatusCode.Created);

            return new Masters(main, shop, await product.Content.ReadFromJsonAsync<Guid>());
        }

        private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync($"{Inventory}/{resource}", body);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

            return await response.Content.ReadFromJsonAsync<Guid>();
        }
    }
}
