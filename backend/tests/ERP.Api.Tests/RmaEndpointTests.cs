using System.Net;
using System.Net.Http.Json;
using ERP.Api.Controllers;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Application.Service;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Service;

namespace ERP.Api.Tests;

/// <summary>
/// Return merchandise authorisations: a customer's return taken through approval and
/// inspection to a credit note, a supplier's exchange that moves the goods between godowns,
/// and the refusals in between.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RmaEndpointTests
{
    private const string Rmas = "/api/v1/service/rmas";

    // A one-pixel PNG: enough to be a picture.
    private const string Photo =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="RmaEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public RmaEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_customers_return_is_approved_inspected_and_refunded_with_a_credit_note()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await ReceiveAsync(client, masters, masters.WarehouseId, 10m);
        Guid sale = await SellAsync(client, masters, 2m, 40m);
        (await OnHandAsync(client, masters, masters.WarehouseId)).ShouldBe(8m);

        // A customer's return goes against a customer, not a supplier.
        (await client.PostAsJsonAsync(Rmas, Request(masters, RmaDirection.FromCustomer, masters.SupplierId)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Rmas, Request(masters, RmaDirection.FromCustomer, masters.CustomerId, sale: sale));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        // Not received before it is approved.
        (await client.PostAsJsonAsync($"{Rmas}/{id}/receive", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await StepAsync(client, id, "approve", "Within warranty");
        await StepAsync(client, id, "receive", "Collected at the counter");

        // What was asked for can no longer change once approved.
        (await client.PutAsJsonAsync($"{Rmas}/{id}", Request(masters, RmaDirection.FromCustomer, masters.CustomerId)))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await client.PostAsJsonAsync($"{Rmas}/{id}/inspect", new
        {
            Condition = RmaCondition.Defective,
            Notes = "No power; board fault",
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage photo = await client.PostAsJsonAsync(
            $"{Rmas}/{id}/photos", new { Content = Photo, Caption = "Back panel" });
        photo.StatusCode.ShouldBe(HttpStatusCode.Created, await photo.Content.ReadAsStringAsync());
        Guid photoId = await photo.Content.ReadFromJsonAsync<Guid>();
        (await client.PostAsJsonAsync($"{Rmas}/{id}/photos", new { Content = "not a picture" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<RmaPhotoContent>($"{Rmas}/{id}/photos/{photoId}"))!
            .Content.ShouldBe(Photo);

        // Refunded with a draft credit note at what the customer paid; posting it brings the
        // unit back.
        HttpResponseMessage refunded = await client.PostAsJsonAsync($"{Rmas}/{id}/refund", new { });
        refunded.StatusCode.ShouldBe(HttpStatusCode.OK, await refunded.Content.ReadAsStringAsync());
        RmaOutcomeResponse credit = (await refunded.Content.ReadFromJsonAsync<RmaOutcomeResponse>())!;
        credit.DocumentId.ShouldNotBeNull();

        SalesInvoiceDetail note = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"/api/v1/sales/invoices/{credit.DocumentId}"))!;
        note.Header.Status.ShouldBe(SalesInvoiceStatus.Draft);
        note.Header.Taxable.ShouldBe(40m);

        (await client.PostAsJsonAsync($"/api/v1/sales/invoices/{credit.DocumentId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OnHandAsync(client, masters, masters.WarehouseId)).ShouldBe(9m);

        await StepAsync(client, id, "close", "Settled");

        RmaDetail rma = (await client.GetFromJsonAsync<RmaDetail>($"{Rmas}/{id}"))!;
        rma.Summary.Status.ShouldBe(RmaStatus.Closed);
        rma.Summary.Condition.ShouldBe(RmaCondition.Defective);
        rma.Summary.OutcomeNumber.ShouldBe(credit.Number);
        rma.SalesInvoiceId.ShouldBe(sale);
        rma.CreditNoteId.ShouldBe(credit.DocumentId);
        rma.Photos.Single().Caption.ShouldBe("Back panel");
        rma.History.Select(entry => entry.Status).ShouldBe(
        [
            RmaStatus.Requested, RmaStatus.Approved, RmaStatus.Received,
            RmaStatus.UnderInspection, RmaStatus.Refunded, RmaStatus.Closed,
        ]);
        rma.History.ShouldAllBe(entry => entry.UserName.Length > 0);

        // Photographs are kept, but nothing more can be added to a closed return.
        (await client.PostAsJsonAsync($"{Rmas}/{id}/photos", new { Content = Photo }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        List<RmaSummary> listed = (await client.GetFromJsonAsync<List<RmaSummary>>(
            $"{Rmas}?status={(int)RmaStatus.Closed}&search={rma.Summary.Number}"))!;
        listed.Single().Quantity.ShouldBe(1m);
    }

    [Fact]
    public async Task A_suppliers_exchange_moves_the_goods_back_from_the_returns_godown()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        Guid returns = await GodownAsync(client);
        await ReceiveAsync(client, masters, returns, 3m);

        Guid id = await (await client.PostAsJsonAsync(
            Rmas, Request(masters, RmaDirection.ToSupplier, masters.SupplierId, warehouse: returns, quantity: 2m)))
            .Content.ReadFromJsonAsync<Guid>();

        await StepAsync(client, id, "approve", null);
        await StepAsync(client, id, "receive", "Handed to the supplier's driver");

        HttpResponseMessage replaced = await client.PostAsJsonAsync(
            $"{Rmas}/{id}/replace", new { StockWarehouseId = masters.WarehouseId });
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
        RmaOutcomeResponse transfer = (await replaced.Content.ReadFromJsonAsync<RmaOutcomeResponse>())!;
        transfer.Number.ShouldNotBeNull();

        (await OnHandAsync(client, masters, returns)).ShouldBe(1m);
        (await OnHandAsync(client, masters, masters.WarehouseId)).ShouldBe(2m);

        RmaDetail rma = (await client.GetFromJsonAsync<RmaDetail>($"{Rmas}/{id}"))!;
        rma.Summary.Status.ShouldBe(RmaStatus.Replaced);
        rma.ReplacementDocumentId.ShouldBe(transfer.DocumentId);

        // Settled once.
        (await client.PostAsJsonAsync($"{Rmas}/{id}/refund", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_return_to_a_supplier_is_refunded_with_a_debit_note_that_sends_the_goods_out()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await ReceiveAsync(client, masters, masters.WarehouseId, 5m);

        Guid id = await (await client.PostAsJsonAsync(
            Rmas, Request(masters, RmaDirection.ToSupplier, masters.SupplierId, quantity: 2m)))
            .Content.ReadFromJsonAsync<Guid>();

        await StepAsync(client, id, "approve", null);
        await StepAsync(client, id, "receive", null);

        HttpResponseMessage refunded = await client.PostAsJsonAsync($"{Rmas}/{id}/refund", new { });
        refunded.StatusCode.ShouldBe(HttpStatusCode.OK, await refunded.Content.ReadAsStringAsync());
        RmaOutcomeResponse debit = (await refunded.Content.ReadFromJsonAsync<RmaOutcomeResponse>())!;

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"/api/v1/purchase/invoices/{debit.DocumentId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        (await OnHandAsync(client, masters, masters.WarehouseId)).ShouldBe(3m);

        RmaDetail rma = (await client.GetFromJsonAsync<RmaDetail>($"{Rmas}/{id}"))!;
        rma.Summary.Status.ShouldBe(RmaStatus.Refunded);
        rma.DebitNoteId.ShouldBe(debit.DocumentId);

        // Its debit note cancelled, the return is open to be settled again.
        (await client.PostAsJsonAsync(
            $"/api/v1/purchase/invoices/{debit.DocumentId}/cancel", new { Reason = "Supplier sent a replacement" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(
            $"{Rmas}/{id}/replace", new { StockWarehouseId = masters.WarehouseId }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync($"{Rmas}/{id}/refund", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refusal_needs_a_reason_and_ends_the_return()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        Guid id = await (await client.PostAsJsonAsync(
            Rmas, Request(masters, RmaDirection.FromCustomer, masters.CustomerId)))
            .Content.ReadFromJsonAsync<Guid>();

        (await client.PostAsJsonAsync($"{Rmas}/{id}/reject", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await StepAsync(client, id, "reject", "Physical damage, not covered");

        (await client.PostAsJsonAsync($"{Rmas}/{id}/approve", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        RmaDetail rma = (await client.GetFromJsonAsync<RmaDetail>($"{Rmas}/{id}"))!;
        rma.Summary.Status.ShouldBe(RmaStatus.Rejected);
        rma.History[^1].Note.ShouldBe("Physical damage, not covered");
    }

    private static object Request(
        TestMasters masters,
        RmaDirection direction,
        Guid party,
        Guid? warehouse = null,
        Guid? sale = null,
        decimal quantity = 1m) => new
        {
            Date = Today,
            Direction = direction,
            PartyLedgerId = party,
            WarehouseId = warehouse ?? masters.WarehouseId,
            Reason = "Does not switch on",
            InvoiceReference = "INV-123",
            SalesInvoiceId = sale,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, SerialNumber = "SN-0001" } },
        };

    private static async Task StepAsync(HttpClient client, Guid id, string step, string? note)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Rmas}/{id}/{step}", new { Note = note });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> GodownAsync(HttpClient client)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/inventory/warehouses", new { Code = $"RET{suffix}", Name = $"Returns {suffix}" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> SellAsync(HttpClient client, TestMasters masters, decimal quantity, decimal rate)
    {
        HttpResponseMessage sold = await client.PostAsJsonAsync("/api/v1/sales/invoices", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = rate, TaxPercentage = 0m } },
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid id = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        (await client.PostAsJsonAsync($"/api/v1/sales/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return id;
    }

    private static async Task ReceiveAsync(HttpClient client, TestMasters masters, Guid warehouse, decimal quantity)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/inventory/stock/documents", new
        {
            Type = StockDocumentType.MaterialReceipt,
            Date = Today,
            WarehouseId = warehouse,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 20m } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<decimal> OnHandAsync(HttpClient client, TestMasters masters, Guid warehouse) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
            $"/api/v1/inventory/stock/valuation?warehouseId={warehouse}&includeZero=true"))!
            .Rows.Where(row => row.ProductId == masters.ProductId)
            .Sum(row => row.Quantity);
}
