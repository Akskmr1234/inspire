using System.Net;
using System.Net.Http.Json;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Sales;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

/// <summary>
/// A sales return is kept in tables of its own - the Sales Return master, details and
/// additional - apart from the sales, while the screens and the API see it as before.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SalesReturnTablesEndpointTests
{
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="SalesReturnTablesEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public SalesReturnTablesEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_return_is_kept_in_the_return_tables_and_read_as_before()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"V-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        PurchaseInvoiceResponse purchase = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{purchase.PurchaseInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 4m, Rate = 20m, TaxPercentage = 5m } },
            Post = true,
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse sale = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        // A draft return of two lines, cut to one: the line that goes takes its tax with it.
        HttpResponseMessage drafted = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = sale.SalesInvoiceId,
            Lines = new[]
            {
                new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 5m },
                new { masters.ProductId, Quantity = 1m, Rate = 20m, TaxPercentage = 5m },
            },
        });
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
        SalesInvoiceResponse draft = (await drafted.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        (await CountsAsync(draft.SalesInvoiceId)).ShouldBe(new Counts(0, 1, 2, 2, 0));

        HttpResponseMessage revised = await client.PutAsJsonAsync($"{Invoices}/{draft.SalesInvoiceId}", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            ReturnsInvoiceId = sale.SalesInvoiceId,
            Lines = new[] { new { masters.ProductId, Quantity = 2m, Rate = 20m, TaxPercentage = 5m } },
        });
        revised.StatusCode.ShouldBe(HttpStatusCode.OK, await revised.Content.ReadAsStringAsync());
        (await client.PostAsJsonAsync($"{Invoices}/{draft.SalesInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // In the return tables, and nowhere among the sales.
        (await CountsAsync(draft.SalesInvoiceId)).ShouldBe(new Counts(0, 1, 1, 1, 0));
        (await CountsAsync(sale.SalesInvoiceId)).ShouldBe(new Counts(1, 0, 0, 0, 1));

        // Read as it always was: one document, its kind, the sale it is against, its figures.
        SalesInvoiceDetail read = (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{draft.SalesInvoiceId}"))!;
        read.Kind.ShouldBe(SalesDocumentKind.Return);
        read.ReturnsInvoiceId.ShouldBe(sale.SalesInvoiceId);
        read.Header.Total.ShouldBe(42m);
        read.Lines.ShouldHaveSingleItem().Taxable.ShouldBe(40m);
        read.Lines[0].Components.ShouldHaveSingleItem().Amount.ShouldBe(2m);

        // A return is against a sale, and a return is no sale.
        HttpResponseMessage againstReturn = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = draft.SalesInvoiceId,
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
                        (SELECT count(*)::int FROM sales_invoices WHERE id = {0}) AS "Sales",
                        (SELECT count(*)::int FROM sales_returns WHERE id = {0}) AS "Returns",
                        (SELECT count(*)::int FROM sales_return_lines WHERE sales_return_id = {0}) AS "ReturnLines",
                        (SELECT count(*)::int FROM sales_return_line_taxes t
                            JOIN sales_return_lines l ON l.id = t.sales_return_line_id
                            WHERE l.sales_return_id = {0}) AS "ReturnTaxes",
                        (SELECT count(*)::int FROM sales_invoice_lines WHERE sales_invoice_id = {0}) AS "SaleLines"
                    """,
                    id)
                .SingleAsync());

        return counts!;
    }

    /// <summary>Where a document's rows are kept.</summary>
    /// <param name="Sales">Its rows in the sales master.</param>
    /// <param name="Returns">Its rows in the return master.</param>
    /// <param name="ReturnLines">Its rows in the return details.</param>
    /// <param name="ReturnTaxes">The tax rows on those.</param>
    /// <param name="SaleLines">Its rows in the sales details.</param>
    public sealed record Counts(int Sales, int Returns, int ReturnLines, int ReturnTaxes, int SaleLines);
}
