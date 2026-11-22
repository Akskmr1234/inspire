using System.Net;
using System.Net.Http.Json;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>The point of sale: a cashier's shift, what it took, and the close of the day.</summary>
[Collection(ApiCollection.Name)]
public sealed class PosEndpointTests
{
    private const string Pos = "/api/v1/sales/pos";
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="PosEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public PosEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_shift_counts_its_takings_and_the_day_closes_only_once_every_shift_has()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);

        // Opened with a float of 100.
        HttpResponseMessage opened = await client.PostAsJsonAsync($"{Pos}/shifts", new { OpeningCash = 100m });
        opened.StatusCode.ShouldBe(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        PosShiftView shift = (await opened.Content.ReadFromJsonAsync<PosShiftView>())!;
        shift.OpeningCash.ShouldBe(100m);
        shift.Takings.SalesCount.ShouldBe(0);

        // One shift at a time.
        (await client.PostAsJsonAsync($"{Pos}/shifts", new { OpeningCash = 5m }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Two cash sales of 15, one on credit of 15, and one cash refund of 15.
        Guid first = await SellAsync(client, masters, SalesPaymentMode.Cash);
        await SellAsync(client, masters, SalesPaymentMode.Cash);
        await SellAsync(client, masters, SalesPaymentMode.Credit);
        HttpResponseMessage refunded = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = first,
            PaymentMode = SalesPaymentMode.Cash,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 15m, TaxPercentage = 0m } },
            Post = true,
        });
        refunded.StatusCode.ShouldBe(HttpStatusCode.Created, await refunded.Content.ReadAsStringAsync());

        PosStatusResponse status = (await client.GetFromJsonAsync<PosStatusResponse>($"{Pos}/status"))!;
        status.Shift.ShouldNotBeNull();
        status.DayClosed.ShouldBeFalse();
        status.Shift.Takings.SalesCount.ShouldBe(3);
        status.Shift.Takings.ReturnsCount.ShouldBe(1);
        status.Shift.Takings.CashTotal.ShouldBe(15m);
        status.Shift.Takings.SalesTotal.ShouldBe(45m);
        status.Shift.Takings.ReturnsTotal.ShouldBe(15m);
        status.Shift.Takings.CreditTotal.ShouldBe(15m);

        // The drawer should hold the float plus the cash taken, less the cash refunded.
        status.Shift.ExpectedCash.ShouldBe(115m);

        // The day cannot close while the shift is open.
        (await client.PostAsJsonAsync($"{Pos}/day/close", new { Date = status.Today }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage closed = await client.PostAsJsonAsync(
            $"{Pos}/shifts/{shift.ShiftId}/close", new { CountedCash = 113m, Notes = "Two short" });
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        PosShiftView counted = (await closed.Content.ReadFromJsonAsync<PosShiftView>())!;
        counted.ClosedAtUtc.ShouldNotBeNull();
        counted.ExpectedCash.ShouldBe(115m);
        counted.CountedCash.ShouldBe(113m);

        // Now the day closes, once, with its takings frozen.
        HttpResponseMessage dayClosed = await client.PostAsJsonAsync($"{Pos}/day/close", new { Date = status.Today });
        dayClosed.StatusCode.ShouldBe(HttpStatusCode.OK, await dayClosed.Content.ReadAsStringAsync());
        PosDayView day = (await dayClosed.Content.ReadFromJsonAsync<PosDayView>())!;
        day.IsClosed.ShouldBeTrue();
        day.OpenShifts.ShouldBe(0);
        day.Takings.SalesCount.ShouldBeGreaterThanOrEqualTo(3);

        (await client.PostAsJsonAsync($"{Pos}/day/close", new { Date = status.Today }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A closed day opens no more shifts.
        (await client.PostAsJsonAsync($"{Pos}/shifts", new { OpeningCash = 100m }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.GetFromJsonAsync<PosStatusResponse>($"{Pos}/status"))!.DayClosed.ShouldBeTrue();
    }

    private static async Task<Guid> SellAsync(HttpClient client, TestMasters masters, SalesPaymentMode mode)
    {
        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            PaymentMode = mode,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 15m, TaxPercentage = 0m } },
            Post = true,
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        return (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;
    }

    private static async Task BuyAsync(HttpClient client, TestMasters masters)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"P-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
