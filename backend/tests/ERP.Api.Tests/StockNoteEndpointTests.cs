using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>
/// Delivery and receipt notes: goods that move ahead of the invoice, and the invoice that
/// later bills them without moving them again.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StockNoteEndpointTests
{
    private const string Stock = "/api/v1/inventory/stock";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public StockNoteEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_delivery_note_takes_the_goods_and_the_invoice_raised_from_it_only_bills_them()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await MoveAsync(client, StockDocumentType.MaterialReceipt, masters, 10m, rate: 20m);

        // The draft is priced at the retail rate.
        (await client.PutAsJsonAsync($"/api/v1/inventory/products/{masters.ProductId}/rates", new
        {
            CostingMethod = CostingMethod.AverageRate,
            Cost = 20m,
            RetailRate = 25m,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Not to a supplier, and not without a customer.
        (await NoteAsync(client, StockDocumentType.DeliveryNote, masters, masters.SupplierId, 3m))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        Guid note = await CreatedAsync(
            await NoteAsync(client, StockDocumentType.DeliveryNote, masters, masters.CustomerId, 3m));
        (await OnHandAsync(client, masters)).ShouldBe(7m);

        HttpResponseMessage raised = await client.PostAsJsonAsync(
            $"{Stock}/documents/{note}/sales-invoice", new { Date = Today });
        raised.StatusCode.ShouldBe(HttpStatusCode.Created, await raised.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await raised.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        invoice.Taxable.ShouldBe(75m);

        // Only one invoice per note.
        (await client.PostAsJsonAsync($"{Stock}/documents/{note}/sales-invoice", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        // Billed, and the goods did not leave a second time.
        (await OnHandAsync(client, masters)).ShouldBe(7m);

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}"))!;
        detail.DeliveryNoteId.ShouldBe(note);
        detail.StockDocumentId.ShouldBe(note);

        StockDocumentDetail billedNote = (await client.GetFromJsonAsync<StockDocumentDetail>(
            $"{Stock}/documents/{note}"))!;
        billedNote.InvoiceNumber.ShouldBe(invoice.Number);
        billedNote.PartyLedgerId.ShouldBe(masters.CustomerId);

        // Cancelling the invoice un-bills the note; the delivery still stands.
        (await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/cancel", new { Reason = "Wrong price" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await OnHandAsync(client, masters)).ShouldBe(7m);
        (await client.GetFromJsonAsync<StockDocumentDetail>($"{Stock}/documents/{note}"))!
            .InvoiceNumber.ShouldBeNull();
    }

    [Fact]
    public async Task An_invoice_against_a_note_must_bill_exactly_what_moved()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await MoveAsync(client, StockDocumentType.MaterialReceipt, masters, 10m, rate: 20m);

        Guid note = await CreatedAsync(
            await NoteAsync(client, StockDocumentType.DeliveryNote, masters, masters.CustomerId, 4m));

        SalesInvoiceResponse invoice = (await (await client.PostAsJsonAsync(
            $"{Stock}/documents/{note}/sales-invoice", new { }))
            .Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        // Edited down to three of the four delivered.
        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 3m, Rate = 15m, TaxPercentage = 0m } },
            });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        HttpResponseMessage refused = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/post", new { });
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("StockDocument.NoteQuantityDiffers");
        (await OnHandAsync(client, masters)).ShouldBe(6m);
    }

    [Fact]
    public async Task A_receipt_note_brings_the_goods_in_and_the_purchase_raised_from_it_only_pays_for_them()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        Guid note = await CreatedAsync(
            await NoteAsync(client, StockDocumentType.ReceiptNote, masters, masters.SupplierId, 5m, rate: 12m));
        (await OnHandAsync(client, masters)).ShouldBe(5m);

        HttpResponseMessage raised = await client.PostAsJsonAsync(
            $"{Stock}/documents/{note}/purchase-invoice", new { SupplierInvoiceNumber = "SUP-77" });
        raised.StatusCode.ShouldBe(HttpStatusCode.Created, await raised.Content.ReadAsStringAsync());
        PurchaseInvoiceResponse purchase = (await raised.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;
        purchase.Taxable.ShouldBe(60m);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"/api/v1/purchase/invoices/{purchase.PurchaseInvoiceId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        (await OnHandAsync(client, masters)).ShouldBe(5m);
        (await client.GetFromJsonAsync<StockDocumentDetail>($"{Stock}/documents/{note}"))!
            .InvoiceId.ShouldBe(purchase.PurchaseInvoiceId);
    }

    private static Task<HttpResponseMessage> NoteAsync(
        HttpClient client,
        StockDocumentType type,
        TestMasters masters,
        Guid party,
        decimal quantity,
        decimal rate = 0m) =>
        client.PostAsJsonAsync($"{Stock}/documents", new
        {
            Type = type,
            Date = Today,
            WarehouseId = masters.WarehouseId,
            PartyLedgerId = party,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = rate } },
        });

    private static async Task MoveAsync(
        HttpClient client,
        StockDocumentType type,
        TestMasters masters,
        decimal quantity,
        decimal rate = 0m)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Stock}/documents", new
        {
            Type = type,
            Date = Today,
            WarehouseId = masters.WarehouseId,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = rate } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!.StockDocumentId;
    }

    private static async Task<decimal> OnHandAsync(HttpClient client, TestMasters masters) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
            $"{Stock}/valuation?warehouseId={masters.WarehouseId}&includeZero=true"))!
            .Rows.Where(row => row.ProductId == masters.ProductId)
            .Sum(row => row.Quantity);
}
