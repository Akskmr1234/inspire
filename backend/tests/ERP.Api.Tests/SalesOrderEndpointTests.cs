using System.Net;
using System.Net.Http.Json;
using ERP.Application.Abstractions;
using ERP.Application.Inventory.Stock;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>Tests the sales order endpoints end to end.</summary>
/// <remarks>
/// The first two links of §12.9's chain through the real host: an order taken, confirmed,
/// and converted into an invoice that is then posted the ordinary way. What is checked is
/// the column an order adds - how much of each line has gone out - across a part
/// delivery, a completion, and a cancellation that puts the goods back on order.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class SalesOrderEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Orders = "/api/v1/sales/orders";
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public SalesOrderEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_order_is_entered_as_a_draft_and_read_back_in_full()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Orders, Order(fixtures, quantity: 10m, rate: 50m, taxPercentage: 5m));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        SalesOrderResponse draft =
            (await created.Content.ReadFromJsonAsync<SalesOrderResponse>())!;

        draft.Number.ShouldStartWith("SO");
        draft.Status.ShouldBe(SalesOrderStatus.Draft);
        draft.Taxable.ShouldBe(500m);
        draft.Tax.ShouldBe(25m);
        draft.Total.ShouldBe(525m);

        SalesOrderDetail detail = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{draft.SalesOrderId}"))!;

        SalesOrderLineDetail line = detail.Lines.ShouldHaveSingleItem();

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
    public async Task Converting_a_confirmed_order_raises_a_draft_invoice_for_the_lot()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 10m, 50m, 5m);

        HttpResponseMessage converted = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert", new { });

        converted.StatusCode.ShouldBe(
            HttpStatusCode.Created, await converted.Content.ReadAsStringAsync());

        SalesInvoiceResponse invoice =
            (await converted.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        // A draft, on purpose: posting moves the stock and raises the debt, and that
        // stays its own step.
        invoice.Status.ShouldBe(SalesInvoiceStatus.Draft);
        invoice.Number.ShouldStartWith("SL");
        invoice.Taxable.ShouldBe(500m);
        invoice.Tax.ShouldBe(25m);

        // And the order is finished, because everything on it went out.
        SalesOrderDetail detail = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        detail.Header.Status.ShouldBe(SalesOrderStatus.Completed);
        detail.Lines[0].OutstandingQuantity.ShouldBe(0m);
    }

    [Fact]
    public async Task An_order_can_be_filled_across_two_deliveries()
    {
        // The reason the line carries an invoiced quantity at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 10m, 50m, 0m);

        SalesOrderDetail before = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        Guid lineId = before.Lines[0].SalesOrderLineId;

        HttpResponseMessage first = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert",
            new { Lines = new[] { new { SalesOrderLineId = lineId, Quantity = 4m } } });

        first.StatusCode.ShouldBe(
            HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        SalesInvoiceResponse firstInvoice =
            (await first.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        firstInvoice.Taxable.ShouldBe(200m);

        SalesOrderDetail midway = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        midway.Header.Status.ShouldBe(SalesOrderStatus.Confirmed);
        midway.Lines[0].InvoicedQuantity.ShouldBe(4m);
        midway.Lines[0].OutstandingQuantity.ShouldBe(6m);

        // The second delivery takes what is left without being told how much.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert", new { });

        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        SalesInvoiceResponse secondInvoice =
            (await second.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        secondInvoice.Taxable.ShouldBe(300m);

        SalesOrderDetail after = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        after.Header.Status.ShouldBe(SalesOrderStatus.Completed);
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

        SalesOrderDetail detail = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        HttpResponseMessage converted = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/convert",
            new
            {
                Lines = new[]
                {
                    new { SalesOrderLineId = detail.Lines[0].SalesOrderLineId, Quantity = 9m },
                },
            });

        converted.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_invoice_from_an_order_posts_and_moves_the_stock()
    {
        // The whole chain: order, confirm, convert, post. The invoice behaves like any
        // other once it exists, which is the point of the conversion producing a draft.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 3m, 100m, 5m);

        SalesInvoiceResponse invoice =
            (await (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
                .Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Invoices}/{invoice.SalesInvoiceId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        PostSalesInvoiceResponse result =
            (await posted.Content.ReadFromJsonAsync<PostSalesInvoiceResponse>())!;

        result.Total.ShouldBe(315m);
        result.StockDocumentNumber.ShouldStartWith("SI");
    }

    [Fact]
    public async Task Cancelling_an_invoice_puts_the_goods_back_on_the_order()
    {
        // The reason the invoice remembers the order it came from. Without it the order
        // would go on believing goods had gone out against an invoice that no longer
        // exists, and the outstanding figure a warehouse works from would be short.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 3m, 100m, 0m);

        SalesInvoiceResponse invoice =
            (await (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
                .Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        (await client.PostAsJsonAsync($"{Invoices}/{invoice.SalesInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.GetFromJsonAsync<SalesOrderDetail>($"{Orders}/{orderId}"))!
            .Header.Status.ShouldBe(SalesOrderStatus.Completed);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Invoices}/{invoice.SalesInvoiceId}/cancel",
            new { Reason = "Shipped to the wrong address" });

        cancelled.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        SalesOrderDetail after = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        after.Header.Status.ShouldBe(SalesOrderStatus.Confirmed);
        after.Lines[0].OutstandingQuantity.ShouldBe(3m);
    }

    [Fact]
    public async Task A_closed_order_takes_no_more_invoices_and_keeps_its_reason()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid orderId = await ConfirmedAsync(client, fixtures, 10m, 50m, 0m);

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            $"{Orders}/{orderId}/close", new { Reason = "Customer went elsewhere" });

        closed.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await closed.Content.ReadAsStringAsync());

        SalesOrderDetail detail = (await client.GetFromJsonAsync<SalesOrderDetail>(
            $"{Orders}/{orderId}"))!;

        detail.Header.Status.ShouldBe(SalesOrderStatus.Cancelled);
        detail.ClosureReason.ShouldBe("Customer went elsewhere");

        (await client.PostAsJsonAsync($"{Orders}/{orderId}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_outstanding_filter_shows_what_is_still_owed()
    {
        // What a warehouse asks for every morning.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        OrderFixtures fixtures = await ArrangeAsync(client);

        Guid open = await ConfirmedAsync(client, fixtures, 10m, 50m, 0m);
        Guid filled = await ConfirmedAsync(client, fixtures, 2m, 50m, 0m);

        (await client.PostAsJsonAsync($"{Orders}/{filled}/convert", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        PagedResult<SalesOrderSummary> outstanding =
            (await client.GetFromJsonAsync<PagedResult<SalesOrderSummary>>(
                $"{Orders}?outstandingOnly=true&page=1&pageSize=100"))!;

        outstanding.Items.ShouldContain(row => row.SalesOrderId == open);
        outstanding.Items.ShouldNotContain(row => row.SalesOrderId == filled);

        SalesOrderSummary row = outstanding.Items.Single(
            candidate => candidate.SalesOrderId == open);

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
            CustomerLedgerId = fixtures.CustomerId,
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

        return (await created.Content.ReadFromJsonAsync<SalesOrderResponse>())!.SalesOrderId;
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

    /// <summary>Creates the masters an order needs, and puts stock on the shelf to fill it.</summary>
    private static async Task<OrderFixtures> ArrangeAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateAsync(
            client, "categories", new { Code = $"OCAT{suffix}", Name = $"Order {suffix}" });

        Guid unitId = await CreateAsync(
            client, "units", new { Code = $"OEA{suffix}", Name = "Each" });

        Guid warehouseId = await CreateAsync(
            client, "warehouses", new { Code = $"OWH{suffix}", Name = "Order store" });

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new
            {
                Code = $"ORD{suffix}",
                Description = "A thing to order",
                CategoryId = categoryId,
                StockUnitId = unitId,
                ItemType = 1,
            });

        product.StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        // Enough to fill anything these tests order, so a posting is never refused for
        // short stock in a test that is about something else.
        HttpResponseMessage received = await client.PostAsJsonAsync(
            $"{Inventory}/stock/documents",
            new
            {
                Type = (int)StockDocumentType.MaterialReceipt,
                Date = Today,
                WarehouseId = warehouseId,
                Lines = new[] { new { ProductId = productId, Quantity = 100m, Rate = 25m } },
            });

        received.StatusCode.ShouldBe(HttpStatusCode.Created);

        return new OrderFixtures(
            await CustomerEndpointTests.CreateAsync(client), warehouseId, productId);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private sealed record OrderFixtures(Guid CustomerId, Guid WarehouseId, Guid ProductId);
}
