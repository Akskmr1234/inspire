using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Tests;

/// <summary>
/// A transaction's totals kept on its master row, and each line's on its detail row, for
/// reports and anything else that reads the tables - always the figures the screens show.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StoredTotalsEndpointTests
{
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="StoredTotalsEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public StoredTotalsEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_sales_invoice_keeps_its_totals_as_it_is_edited_and_posted()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        List<ChargeSetup> charges = (await client.GetFromJsonAsync<List<ChargeSetup>>(
            "/api/v1/accounting/charges?document=Sales"))!;
        ChargeSetup freight = charges.Single(charge => charge.Code == "FREIGHT");
        ChargeSetup discount = charges.Single(charge => charge.Code == "DISC-ALLOWED");
        await BuyAsync(client, masters);

        object Sale(decimal quantity) => new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Mode = TaxMode.Tax,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 33.335m, TaxPercentage = 5m } },
            Charges = new[] { new { freight.LedgerId, Amount = 20m }, new { discount.LedgerId, Amount = 7.5m } },
        };

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(3m));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        Stored stored = await StoredAsync("sales_invoices", invoice.SalesInvoiceId);
        stored.ShouldBe(new Stored(100.01m, 5.00m, 12.5m, 117.51m, 0m, 117.51m));
        (await LineTotalsAsync("sales_invoice_lines", "sales_invoice_id", invoice.SalesInvoiceId))
            .ShouldBe([105.01m]);

        // Edited, the totals follow the new lines.
        HttpResponseMessage edited = await client.PutAsJsonAsync($"{Invoices}/{invoice.SalesInvoiceId}", Sale(2m));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"{Invoices}/{invoice.SalesInvoiceId}"))!;
        Stored afterEdit = await StoredAsync("sales_invoices", invoice.SalesInvoiceId);
        afterEdit.Total.ShouldBe(detail.Header.Total);
        afterEdit.Taxable.ShouldBe(66.67m);

        // Posted - a change of state alone - they are left exactly as they were.
        (await client.PostAsJsonAsync($"{Invoices}/{invoice.SalesInvoiceId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoredAsync("sales_invoices", invoice.SalesInvoiceId)).ShouldBe(afterEdit);
    }

    [Fact]
    public async Task Purchases_and_orders_keep_their_totals_too()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"T-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 4m, Rate = 12.5m, TaxPercentage = 5m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        PurchaseInvoiceResponse purchase = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;
        (await StoredAsync("purchase_invoices", purchase.PurchaseInvoiceId))
            .ShouldBe(new Stored(purchase.Taxable, purchase.Tax, purchase.ChargeTotal, 52.5m, purchase.RoundingDifference, purchase.Total));

        HttpResponseMessage ordered = await client.PostAsJsonAsync("/api/v1/sales/orders", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            ExpectedOn = Today.AddDays(7),
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 50m, TaxPercentage = 5m } },
        });
        ordered.StatusCode.ShouldBe(HttpStatusCode.Created, await ordered.Content.ReadAsStringAsync());
        SalesOrderResponse order = (await ordered.Content.ReadFromJsonAsync<SalesOrderResponse>())!;
        (await StoredAsync("sales_orders", order.SalesOrderId)).Total.ShouldBe(525m);
        (await LineTotalsAsync("sales_order_lines", "sales_order_id", order.SalesOrderId)).ShouldBe([525m]);

        HttpResponseMessage placed = await client.PostAsJsonAsync("/api/v1/purchase/orders", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            ExpectedOn = Today.AddDays(7),
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 50m, TaxPercentage = 5m } },
        });
        placed.StatusCode.ShouldBe(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        PurchaseOrderResponse purchaseOrder = (await placed.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        (await StoredAsync("purchase_orders", purchaseOrder.PurchaseOrderId)).Total.ShouldBe(525m);
    }

    [Fact]
    public async Task Totals_missing_from_older_transactions_are_filled_in_without_touching_them()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 2m, Rate = 15m, TaxPercentage = 0m } },
        });
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        Stored before = await StoredAsync("sales_invoices", invoice.SalesInvoiceId);

        // As a transaction saved before the totals were kept looks.
        DateTimeOffset? modified = null;
        await _factory.WithServiceAsync<ErpDbContext>(async context =>
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                UPDATE sales_invoices SET taxable_total = NULL, tax_total = NULL, charge_total = NULL,
                    gross_total = NULL, rounding_difference = NULL, total = NULL WHERE id = {0}
                """,
                invoice.SalesInvoiceId);
            modified = await context.Database
                .SqlQueryRaw<DateTimeOffset?>(
                    "SELECT modified_at_utc AS \"Value\" FROM sales_invoices WHERE id = {0}", invoice.SalesInvoiceId)
                .SingleAsync();
        });
        (await StoredAsync("sales_invoices", invoice.SalesInvoiceId)).Total.ShouldBeNull();

        (await _factory.Services.GetRequiredService<StoredTotalsBackfill>().FillAllAsync()).ShouldBeGreaterThan(0);

        (await StoredAsync("sales_invoices", invoice.SalesInvoiceId)).ShouldBe(before);
        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            (await context.Database
                .SqlQueryRaw<DateTimeOffset?>(
                    "SELECT modified_at_utc AS \"Value\" FROM sales_invoices WHERE id = {0}", invoice.SalesInvoiceId)
                .SingleAsync()).ShouldBe(modified));
    }

    private static readonly HashSet<string> Tables =
    [
        "sales_invoices", "purchase_invoices", "sales_orders", "purchase_orders",
        "sales_invoice_lines", "sales_order_lines", "sales_invoice_id", "sales_order_id",
    ];

    private async Task<Stored> StoredAsync(string table, Guid id)
    {
        Tables.ShouldContain(table);
        string sql = "SELECT taxable_total AS \"Taxable\", tax_total AS \"Tax\", charge_total AS \"Charges\", "
            + "gross_total AS \"Gross\", rounding_difference AS \"Rounding\", total AS \"Total\" FROM "
            + table + " WHERE id = {0}";
        Stored? stored = null;

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            stored = await context.Database.SqlQueryRaw<Stored>(sql, id).SingleAsync());

        return stored!;
    }

    private async Task<List<decimal>> LineTotalsAsync(string table, string parent, Guid id)
    {
        Tables.ShouldContain(table);
        Tables.ShouldContain(parent);
        string sql = "SELECT line_total AS \"Value\" FROM " + table + " WHERE " + parent + " = {0} ORDER BY line_number";
        List<decimal> totals = [];

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            totals = await context.Database.SqlQueryRaw<decimal>(sql, id).ToListAsync());

        return totals;
    }

    private static async Task BuyAsync(HttpClient client, TestMasters masters)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"S-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>A master's stored totals.</summary>
    /// <param name="Taxable">The goods before tax.</param>
    /// <param name="Tax">The tax.</param>
    /// <param name="Charges">The charges, net.</param>
    /// <param name="Gross">Before rounding.</param>
    /// <param name="Rounding">The rounding difference.</param>
    /// <param name="Total">The total.</param>
    public sealed record Stored(
        decimal? Taxable,
        decimal? Tax,
        decimal? Charges,
        decimal? Gross,
        decimal? Rounding,
        decimal? Total);
}
