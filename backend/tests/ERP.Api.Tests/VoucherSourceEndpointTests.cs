using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Tests;

/// <summary>
/// The common voucher table leads back to the transaction behind every debit and credit:
/// each voucher a sale, a purchase or a return generates says which one it came from.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class VoucherSourceEndpointTests
{
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="VoucherSourceEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public VoucherSourceEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Each_voucher_names_the_sale_purchase_or_return_it_came_from()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        // A purchase, posted: its journal is the purchase's.
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

        Source[] purchased = await SourcesAsync("purchase_invoices", purchase.PurchaseInvoiceId);
        purchased.ShouldBe([new Source("Purchase", purchase.PurchaseInvoiceId, purchase.Number)]);

        // A cash sale: its journal - and a receipt, where one is raised - is the sale's.
        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            PaymentMode = SalesPaymentMode.Cash,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 15m, TaxPercentage = 0m } },
            Post = true,
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse sale = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        (await SourcesAsync("sales_invoices", sale.SalesInvoiceId))
            .ShouldAllBe(source => source == new Source("Sales", sale.SalesInvoiceId, sale.Number));

        // A return of it is a return.
        HttpResponseMessage returned = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 2,
            ReturnsInvoiceId = sale.SalesInvoiceId,
            PaymentMode = SalesPaymentMode.Cash,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 15m, TaxPercentage = 0m } },
            Post = true,
        });
        returned.StatusCode.ShouldBe(HttpStatusCode.Created, await returned.Content.ReadAsStringAsync());
        SalesInvoiceResponse refund = (await returned.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        (await SourcesAsync("sales_invoices", refund.SalesInvoiceId))
            .ShouldAllBe(source => source == new Source("SalesReturn", refund.SalesInvoiceId, refund.Number));

        // A payment entered as itself is its own transaction, and names nothing else.
        List<LedgerSummary> ledgers = (await client.GetFromJsonAsync<List<LedgerSummary>>("/api/v1/accounting/ledgers"))!;
        Guid cash = ledgers.First(ledger => ledger.Kind == LedgerKind.Cash).LedgerId;
        HttpResponseMessage paid = await client.PostAsJsonAsync("/api/v1/accounting/vouchers", new
        {
            Type = VoucherType.CashPayment,
            Date = Today,
            PostImmediately = true,
            Lines = new object[]
            {
                new { LedgerId = masters.SupplierId, Side = EntrySide.Debit, Amount = 40m },
                new { LedgerId = cash, Side = EntrySide.Credit, Amount = 40m },
            },
        });
        paid.StatusCode.ShouldBe(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());
        Guid payment = (await paid.Content.ReadFromJsonAsync<CreateVoucherResponse>())!.VoucherId;
        (await SourceOfAsync(payment)).ShouldBe(new Source(null, null, null));
    }

    private static readonly HashSet<string> Masters = ["sales_invoices", "purchase_invoices"];

    /// <summary>What each voucher a document names says it came from.</summary>
    private async Task<Source[]> SourcesAsync(string table, Guid id)
    {
        Masters.ShouldContain(table);
        string receipt = table == "sales_invoices" ? ", d.receipt_voucher_id" : string.Empty;
        string sql = "SELECT v.source_type AS \"Type\", v.source_id AS \"Id\", v.source_number AS \"Number\" "
            + "FROM vouchers v JOIN " + table + " d ON v.id IN (d.journal_voucher_id" + receipt + ") "
            + "WHERE d.id = {0}";
        Source[] sources = [];

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            sources = [.. await context.Database.SqlQueryRaw<Source>(sql, id).ToListAsync()]);

        sources.ShouldNotBeEmpty();
        return sources;
    }

    private async Task<Source> SourceOfAsync(Guid voucherId)
    {
        Source? source = null;

        await _factory.WithServiceAsync<ErpDbContext>(async context =>
            source = await context.Database
                .SqlQueryRaw<Source>(
                    "SELECT source_type AS \"Type\", source_id AS \"Id\", source_number AS \"Number\" FROM vouchers WHERE id = {0}",
                    voucherId)
                .SingleAsync());

        return source!;
    }

    /// <summary>What a voucher says it came from.</summary>
    /// <param name="Type">The kind of transaction.</param>
    /// <param name="Id">The transaction.</param>
    /// <param name="Number">Its number.</param>
    public sealed record Source(string? Type, Guid? Id, string? Number);
}
