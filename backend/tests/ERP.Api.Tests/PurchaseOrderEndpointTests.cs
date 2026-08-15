using System.Net;
using System.Net.Http.Json;
using ERP.Application.Abstractions;
using ERP.Application.Purchase;
using ERP.Domain.Purchase;

namespace ERP.Api.Tests;

/// <summary>Tests the purchase order endpoints end to end.</summary>
/// <remarks>
/// The purchase side of §12.9's chain through the real host: an order placed, confirmed, and
/// converted into a purchase that is then posted the ordinary way. What is checked is the
/// column an order adds - how much of each line has arrived - across a part delivery, a
/// completion, and a cancellation that puts the goods back on order.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class PurchaseOrderEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Orders = "/api/v1/purchase/orders";
    private const string Purchases = "/api/v1/purchase/invoices";
    private const string Suppliers = "/api/v1/purchase/suppliers";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public PurchaseOrderEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_order_is_placed_as_a_draft_and_read_back_in_full()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Orders, Order(fixtures, quantity: 10m, rate: 50m, taxPercentage: 5m));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        PurchaseOrderResponse draft =
            (await created.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        draft.Number.ShouldStartWith("PO");
        draft.Status.ShouldBe(PurchaseOrderStatus.Draft);
        draft.Taxable.ShouldBe(500m);
        draft.Tax.ShouldBe(25m);
        draft.Total.ShouldBe(525m);

        PurchaseOrderDetail detail = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{draft.PurchaseOrderId}"))!;

        PurchaseOrderLineDetail line = detail.Lines.ShouldHaveSingleItem();

        line.Quantity.ShouldBe(10m);
        line.InvoicedQuantity.ShouldBe(0m);
        line.OutstandingQuantity.ShouldBe(10m);
    }

    [Fact]
    public async Task A_draft_cannot_be_converted_until_it_is_confirmed()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await EnterAsync(client, fixtures, 10m, 50m);

        (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Converting_a_confirmed_order_raises_a_draft_purchase_for_the_lot()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 10m, 50m, 5m);

        HttpResponseMessage converted = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert", new { });

        converted.StatusCode.ShouldBe(
            HttpStatusCode.Created, await converted.Content.ReadAsStringAsync());

        PurchaseInvoiceResponse purchase =
            (await converted.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        // A draft, on purpose: posting receives the goods and raises the debt, and that
        // stays its own step.
        purchase.Status.ShouldBe(PurchaseInvoiceStatus.Draft);
        purchase.Number.ShouldStartWith("PU");
        purchase.Taxable.ShouldBe(500m);
        purchase.Tax.ShouldBe(25m);

        // And the order is finished, because everything on it arrived.
        PurchaseOrderDetail detail = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        detail.Header.Status.ShouldBe(PurchaseOrderStatus.Completed);
        detail.Lines[0].OutstandingQuantity.ShouldBe(0m);
    }

    [Fact]
    public async Task An_order_can_be_filled_across_two_deliveries()
    {
        // The reason the line carries an invoiced quantity at all, and the routine case on
        // the purchase side rather than the exception.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 10m, 50m, 0m);

        PurchaseOrderDetail before = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        Guid lineId = before.Lines[0].PurchaseOrderLineId;

        HttpResponseMessage first = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert",
            new { Lines = new[] { new { PurchaseOrderLineId = lineId, Quantity = 4m } } });

        first.StatusCode.ShouldBe(
            HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        PurchaseInvoiceResponse firstPurchase =
            (await first.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        firstPurchase.Taxable.ShouldBe(200m);

        PurchaseOrderDetail midway = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        midway.Header.Status.ShouldBe(PurchaseOrderStatus.Confirmed);
        midway.Lines[0].InvoicedQuantity.ShouldBe(4m);
        midway.Lines[0].OutstandingQuantity.ShouldBe(6m);

        // The second delivery takes what is left without being told how much.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert", new { });

        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        PurchaseInvoiceResponse secondPurchase =
            (await second.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        secondPurchase.Taxable.ShouldBe(300m);

        PurchaseOrderDetail after = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        after.Header.Status.ShouldBe(PurchaseOrderStatus.Completed);
    }

    [Fact]
    public async Task A_completed_order_has_nothing_left_to_convert()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 5m, 50m, 0m);

        (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_line_cannot_be_converted_for_more_than_was_ordered()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 5m, 50m, 0m);

        PurchaseOrderDetail detail = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        HttpResponseMessage converted = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert",
            new
            {
                Lines = new[]
                {
                    new
                    {
                        PurchaseOrderLineId = detail.Lines[0].PurchaseOrderLineId,
                        Quantity = 9m,
                    },
                },
            });

        converted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_purchase_from_an_order_posts_and_puts_the_goods_on_the_shelf()
    {
        // The whole chain: order, confirm, convert, post. The purchase behaves like any
        // other once it exists, which is the point of the conversion producing a draft.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 4m, 25m, 5m);

        PurchaseInvoiceResponse purchase =
            (await (await client.PostAsJsonAsync(
                $"{Orders}/{orderId}/convert",
                new { SupplierInvoiceNumber = $"INV-{Guid.NewGuid():N}"[..14] }))
                .Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Purchases}/{purchase.PurchaseInvoiceId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        PostPurchaseInvoiceResponse result =
            (await posted.Content.ReadFromJsonAsync<PostPurchaseInvoiceResponse>())!;

        result.Total.ShouldBe(105m);
    }

    [Fact]
    public async Task The_same_supplier_invoice_number_is_refused_on_a_conversion()
    {
        // Keying it twice is reclaiming its input tax twice. Asked here as well as on a
        // directly-entered purchase, because a conversion reaches the same ledger.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        string supplierNumber = $"DUP-{Guid.NewGuid():N}"[..14];

        Guid first = await ConfirmedAsync(client, fixtures, 2m, 25m, 0m);

        (await client.PostAsJsonAsync(
            $"{Orders}/{first}/convert", new { SupplierInvoiceNumber = supplierNumber }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid second = await ConfirmedAsync(client, fixtures, 2m, 25m, 0m);

        (await client.PostAsJsonAsync(
            $"{Orders}/{second}/convert", new { SupplierInvoiceNumber = supplierNumber }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_a_purchase_puts_the_goods_back_on_the_order()
    {
        // The reason the purchase remembers the order it came from. Without it the order
        // would go on believing goods had arrived against a purchase that no longer
        // exists, and the figure a buyer chases from would be short.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 3m, 100m, 0m);

        PurchaseInvoiceResponse purchase =
            (await (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
                .Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        (await client.PostAsJsonAsync(
            $"{Purchases}/{purchase.PurchaseInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.GetFromJsonAsync<PurchaseOrderDetail>($"{Orders}/{orderId}"))!
            .Header.Status.ShouldBe(PurchaseOrderStatus.Completed);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Purchases}/{purchase.PurchaseInvoiceId}/cancel",
            new { Reason = "The wrong goods were sent" });

        cancelled.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        PurchaseOrderDetail after = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        after.Header.Status.ShouldBe(PurchaseOrderStatus.Confirmed);
        after.Lines[0].OutstandingQuantity.ShouldBe(3m);
    }

    [Fact]
    public async Task A_closed_order_takes_no_more_purchases_and_keeps_its_reason()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 5m, 50m, 0m);

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/close", new { Reason = "The supplier discontinued it" });

        closed.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await closed.Content.ReadAsStringAsync());

        PurchaseOrderDetail detail = (await client.GetFromJsonAsync<PurchaseOrderDetail>(
            $"{Orders}/{orderId}"))!;

        detail.Header.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        detail.ClosureReason.ShouldBe("The supplier discontinued it");

        (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_outstanding_filter_shows_only_orders_still_owed()
    {
        // What a buyer asks for every morning, and the reason the list carries the filter
        // at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid open = await ConfirmedAsync(client, fixtures, 10m, 50m, 0m);
        Guid filled = await ConfirmedAsync(client, fixtures, 2m, 50m, 0m);

        (await client.PostAsJsonAsync($"{Orders}/{filled}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        PagedResult<PurchaseOrderSummary> outstanding =
            (await client.GetFromJsonAsync<PagedResult<PurchaseOrderSummary>>(
                $"{Orders}?outstandingOnly=true&pageSize=200"))!;

        outstanding.Items.ShouldContain(row => row.PurchaseOrderId == open);
        outstanding.Items.ShouldNotContain(row => row.PurchaseOrderId == filled);

        PurchaseOrderSummary row = outstanding.Items.Single(
            candidate => candidate.PurchaseOrderId == open);

        row.OutstandingLines.ShouldBe(1);
        row.Total.ShouldBe(500m);
    }

    // ------------------------------------------------------------------ scaffolding

    private static object Order(
        OrderFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage) =>
        new
        {
            Date = Today,
            SupplierLedgerId = fixtures.SupplierId,
            WarehouseId = fixtures.WarehouseId,
            ExpectedOn = Today.AddDays(7),
            Lines = new[]
            {
                new
                {
                    ProductId = fixtures.ProductId,
                    Quantity = quantity,
                    Rate = rate,
                    TaxPercentage = taxPercentage,
                },
            },
        };

    private static async Task<Guid> EnterAsync(
        HttpClient client,
        OrderFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage = 0m)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Orders, Order(fixtures, quantity, rate, taxPercentage));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!
            .PurchaseOrderId;
    }

    private static async Task<Guid> ConfirmedAsync(
        HttpClient client,
        OrderFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage)
    {
        Guid orderId = await EnterAsync(client, fixtures, quantity, rate, taxPercentage);

        HttpResponseMessage confirmed = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/confirm", new { });

        confirmed.StatusCode.ShouldBe(
            HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());

        return orderId;
    }

    /// <summary>Creates the masters an order needs: a supplier, a warehouse, a product.</summary>
    /// <remarks>
    /// No stock is received first, unlike the sales order's fixtures. A purchase brings the
    /// goods into existence, so there is nothing to put on a shelf beforehand - which is
    /// the whole difference between the two directions.
    /// </remarks>
    private static async Task<OrderFixtures> ArrangeAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateAsync(
            client, "categories", new { Code = $"QCAT{suffix}", Name = $"Order {suffix}" });

        Guid unitId = await CreateAsync(
            client, "units", new { Code = $"QEA{suffix}", Name = "Each" });

        Guid warehouseId = await CreateAsync(
            client, "warehouses", new { Code = $"QWH{suffix}", Name = "Order store" });

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new
            {
                Code = $"POR{suffix}",
                Description = "A thing to order in",
                CategoryId = categoryId,
                StockUnitId = unitId,
                ItemType = 1,
            });

        product.StatusCode.ShouldBe(
            HttpStatusCode.Created, await product.Content.ReadAsStringAsync());

        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage supplier = await client.PostAsJsonAsync(
            Suppliers,
            new { Code = $"QSP{suffix}", Name = $"Gulf Wholesale {suffix}" });

        supplier.StatusCode.ShouldBe(
            HttpStatusCode.Created, await supplier.Content.ReadAsStringAsync());

        Guid supplierId =
            (await supplier.Content.ReadFromJsonAsync<SupplierResponse>())!.SupplierId;

        return new OrderFixtures(supplierId, warehouseId, productId);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private sealed record OrderFixtures(Guid SupplierId, Guid WarehouseId, Guid ProductId);
}
