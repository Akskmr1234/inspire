using System.Net;
using System.Net.Http.Json;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Data;
using ERP.Application.Purchase;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>A firm's data can be cleared, to hand it over or to test from nothing.</summary>
/// <remarks>
/// The firm asked for a way to clear the masters or the transactions for a new customer or
/// for testing. These clear the seeded firm through the real host and check that what
/// setup created is still there to start again with.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DataResetEndpointTests
{
    private const string Reset = "/api/v1/platform/data/reset";
    private const string FirmName = "Inspire Trading";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public DataResetEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Transactions_go_first_and_then_everything_but_the_setup()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6];

        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"R-{tag}",
            Lines = new[] { new { masters.ProductId, Quantity = 4m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid purchaseId = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage sold = await client.PostAsJsonAsync("/api/v1/sales/invoices", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 25m, TaxPercentage = 0m } },
        });
        Guid saleId = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/sales/invoices/{saleId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Not confirmed, nothing happens.
        (await client.PostAsJsonAsync(Reset, new { Scope = DataResetScope.Transactions, Confirmation = "yes" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/v1/sales/invoices/{saleId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage cleared = await client.PostAsJsonAsync(
            Reset, new { Scope = DataResetScope.Transactions, Confirmation = FirmName });
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        DataResetResult result = (await cleared.Content.ReadFromJsonAsync<DataResetResult>())!;
        result.Removed.Keys.ShouldContain("sales_invoices");
        result.Removed.Keys.ShouldContain("vouchers");

        // The documents and the stock are gone; the product and the customer are not.
        (await client.GetAsync($"/api/v1/sales/invoices/{saleId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/purchase/invoices/{purchaseId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/inventory/products/{masters.ProductId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        StockLedgerReport ledger = (await client.GetFromJsonAsync<StockLedgerReport>(
            $"/api/v1/inventory/stock/ledger?productId={masters.ProductId}&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}"))!;
        ledger.ClosingQuantity.ShouldBe(0m);
        ledger.Rows.ShouldBeEmpty();

        // Everything: the product goes too, and the seeded cash ledger stays.
        HttpResponseMessage wiped = await client.PostAsJsonAsync(
            Reset, new { Scope = DataResetScope.Everything, Confirmation = "MAIN" });
        wiped.StatusCode.ShouldBe(HttpStatusCode.OK, await wiped.Content.ReadAsStringAsync());

        (await client.GetAsync($"/api/v1/inventory/products/{masters.ProductId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        List<LedgerRow> ledgers = (await client.GetFromJsonAsync<List<LedgerRow>>("/api/v1/accounting/ledgers?activeOnly=false"))!;
        ledgers.ShouldContain(row => row.Code == "CASH");
        ledgers.ShouldNotContain(row => row.LedgerId == masters.CustomerId);

        // With nothing left, the firm is given its Primary customer, supplier and godown,
        // and a unit, the first time each list is read - and only the once.
        CustomerResponse customer = await PrimaryCustomerAsync(client);
        customer.Code.ShouldBe("PRIMARY-CUSTOMER");
        (await PrimaryCustomerAsync(client)).CustomerId.ShouldBe(customer.CustomerId);

        SupplierResponse supplier = (await client.GetFromJsonAsync<List<SupplierResponse>>("/api/v1/purchase/suppliers"))!
            .ShouldHaveSingleItem();
        supplier.IsPrimary.ShouldBeTrue();

        // The default every sale starts with cannot be withdrawn.
        (await client.PutAsJsonAsync($"/api/v1/sales/customers/{customer.CustomerId}/active", new { IsActive = false }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        List<WarehouseRow> warehouses = (await client.GetFromJsonAsync<List<WarehouseRow>>("/api/v1/inventory/warehouses"))!;
        WarehouseRow godown = warehouses.ShouldHaveSingleItem();
        godown.Name.ShouldBe("Primary");
        godown.IsDefault.ShouldBeTrue();
        (await client.GetFromJsonAsync<List<WarehouseRow>>("/api/v1/inventory/warehouses"))!.ShouldHaveSingleItem();

        (await client.GetFromJsonAsync<List<UnitRow>>("/api/v1/inventory/units"))!
            .ShouldHaveSingleItem().Code.ShouldBe("NOS");

        // A second clear keeps the Primary customer, as it keeps the Primary category.
        (await client.PostAsJsonAsync(Reset, new { Scope = DataResetScope.Everything, Confirmation = "MAIN" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PrimaryCustomerAsync(client)).CustomerId.ShouldBe(customer.CustomerId);

        // And the firm can start again at once.
        await TestMasters.ArrangeAsync(client);
    }

    private static async Task<CustomerResponse> PrimaryCustomerAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<CustomerResponse>>("/api/v1/sales/customers"))!
            .Where(row => row.IsPrimary)
            .ShouldHaveSingleItem();

    private sealed record WarehouseRow(Guid Id, string Code, string Name, bool IsDefault);

    private sealed record UnitRow(Guid Id, string Code, string Name);

    private sealed record LedgerRow(Guid LedgerId, string Code, string Name);
}
