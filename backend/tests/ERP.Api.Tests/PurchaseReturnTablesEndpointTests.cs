using System.Net;
using System.Net.Http.Json;
using ERP.Application.Purchase;
using ERP.Domain.Purchase;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

/// <summary>
/// A purchase return is kept in tables of its own - the Purchase Return master, details
/// and additional - apart from the purchases, while the screens and the API see it as
/// before.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PurchaseReturnTablesEndpointTests
{
    private const string Invoices = "/api/v1/purchase/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="PurchaseReturnTablesEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public PurchaseReturnTablesEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_return_is_kept_in_the_return_tables_and_read_as_before()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        HttpResponseMessage bought = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"V-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 20m, TaxPercentage = 5m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        PurchaseInvoiceResponse purchase = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;
        (await client.PostAsJsonAsync($"{Invoices}/{purchase.PurchaseInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A draft return of two lines, cut to one: the line that goes takes its tax with it.
        HttpResponseMessage drafted = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = purchase.PurchaseInvoiceId,
            SupplierInvoiceNumber = $"R-{Guid.NewGuid():N}"[..12],
            Lines = new[]
            {
                new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 5m },
                new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 5m },
            },
        });
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
        PurchaseInvoiceResponse draft = (await drafted.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;
        (await CountsAsync(draft.PurchaseInvoiceId)).ShouldBe(new Counts(0, 1, 2, 2, 0));

        HttpResponseMessage revised = await client.PutAsJsonAsync($"{Invoices}/{draft.PurchaseInvoiceId}", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = purchase.PurchaseInvoiceId,
            SupplierInvoiceNumber = $"R-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 2m, Rate = 20m, TaxPercentage = 5m } },
        });
        revised.StatusCode.ShouldBe(HttpStatusCode.OK, await revised.Content.ReadAsStringAsync());
        (await client.PostAsJsonAsync($"{Invoices}/{draft.PurchaseInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // In the return tables, and nowhere among the purchases.
        (await CountsAsync(draft.PurchaseInvoiceId)).ShouldBe(new Counts(0, 1, 1, 1, 0));
        (await CountsAsync(purchase.PurchaseInvoiceId)).ShouldBe(new Counts(1, 0, 0, 0, 1));

        // Read as it always was: one document, its kind, the purchase it is against, its figures.
        PurchaseInvoiceDetail read = (await client.GetFromJsonAsync<PurchaseInvoiceDetail>(
            $"{Invoices}/{draft.PurchaseInvoiceId}"))!;
        read.Kind.ShouldBe(PurchaseDocumentKind.Return);
        read.ReturnsInvoiceId.ShouldBe(purchase.PurchaseInvoiceId);
        read.Header.Total.ShouldBe(42m);
        read.Lines.ShouldHaveSingleItem().Taxable.ShouldBe(40m);
        read.Lines[0].Components.ShouldHaveSingleItem().Amount.ShouldBe(2m);

        // A return is against a purchase, and a return is no purchase.
        HttpResponseMessage againstReturn = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = draft.PurchaseInvoiceId,
            SupplierInvoiceNumber = $"R-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 0m } },
        });
        againstReturn.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await againstReturn.Content.ReadAsStringAsync());
    }

    /// <summary>Where a document's rows are kept.</summary>
    private async Task<Counts> CountsAsync(Guid id)
    {
        Counts? counts = null;

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            counts = await context.Database
                .SqlQueryRaw<Counts>(
                    """
                    SELECT
                        (SELECT count(*)::int FROM purchase_invoices WHERE id = {0}) AS "Purchases",
                        (SELECT count(*)::int FROM purchase_returns WHERE id = {0}) AS "Returns",
                        (SELECT count(*)::int FROM purchase_return_lines WHERE purchase_return_id = {0}) AS "ReturnLines",
                        (SELECT count(*)::int FROM purchase_return_line_taxes t
                            JOIN purchase_return_lines l ON l.id = t.purchase_return_line_id
                            WHERE l.purchase_return_id = {0}) AS "ReturnTaxes",
                        (SELECT count(*)::int FROM purchase_invoice_lines WHERE purchase_invoice_id = {0}) AS "PurchaseLines"
                    """,
                    id)
                .SingleAsync());

        return counts!;
    }

    /// <summary>Where a document's rows are kept.</summary>
    /// <param name="Purchases">Its rows in the purchase master.</param>
    /// <param name="Returns">Its rows in the return master.</param>
    /// <param name="ReturnLines">Its rows in the return details.</param>
    /// <param name="ReturnTaxes">The tax rows on those.</param>
    /// <param name="PurchaseLines">Its rows in the purchase details.</param>
    public sealed record Counts(int Purchases, int Returns, int ReturnLines, int ReturnTaxes, int PurchaseLines);
}
