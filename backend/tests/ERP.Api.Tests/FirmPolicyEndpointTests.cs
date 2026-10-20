using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Inventory.Products;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Settings;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>
/// The rules a firm chooses that the server enforces: whether stock may go below zero, and
/// whether a customer's credit limit only warns or blocks the sale.
/// </summary>
/// <remarks>
/// Every test puts the defaults back when it finishes, because every test in the collection
/// shares one firm and a policy left behind would change what the next one is allowed to do.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FirmPolicyEndpointTests
{
    private const string Policies = "/api/v1/settings/policies";
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="FirmPolicyEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public FirmPolicyEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_firm_starts_with_negative_stock_refused_and_a_credit_limit_that_warns()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        FirmPolicies policies = (await client.GetFromJsonAsync<FirmPolicies>(Policies))!;

        policies.NegativeStock.ShouldBe(NegativeStockPolicy.Block);
        policies.CreditLimit.ShouldBe(CreditLimitPolicy.Warn);
        policies.TransfersRequireApproval.ShouldBeFalse();

        // Not an unread document like the screens' preferences: an unknown choice is refused.
        HttpResponseMessage nonsense = await client.PutAsJsonAsync(Policies, new
        {
            NegativeStock = 9,
            CreditLimit = 2,
        });
        nonsense.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nonsense.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stock_goes_below_zero_only_where_the_firm_allows_it_and_comes_back_on_the_next_receipt()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters, quantity: 10m, rate: 10m);

        try
        {
            // Refused by default: fifteen out of ten.
            HttpResponseMessage refused = await client.PostAsJsonAsync(Invoices, Sale(masters, 15m));
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());

            await SetAsync(client, new FirmPolicies(NegativeStock: NegativeStockPolicy.Allow));

            HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, 15m));
            sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());

            StockValuationRow below = await PositionAsync(client, masters);
            below.Quantity.ShouldBe(-5m);
            below.Value.ShouldBe(-50m);

            // The next receipt brings it back, and the value stays the quantity times
            // the average: what the books hold for the shelf.
            await BuyAsync(client, masters, quantity: 10m, rate: 10m);

            StockValuationRow back = await PositionAsync(client, masters);
            back.Quantity.ShouldBe(5m);
            back.Value.ShouldBe(50m);
        }
        finally
        {
            await SetAsync(client, FirmPolicies.Default);
        }
    }

    [Fact]
    public async Task A_credit_limit_set_to_block_refuses_a_sale_on_account_past_it_but_not_one_paid_for()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters, quantity: 10m, rate: 10m);

        HttpResponseMessage limited = await client.PutAsJsonAsync(
            $"/api/v1/sales/customers/{masters.CustomerId}",
            new { Name = $"Limited {Guid.NewGuid():N}"[..20], Terms = new { CreditLimit = 20m } });
        limited.StatusCode.ShouldBe(HttpStatusCode.OK, await limited.Content.ReadAsStringAsync());

        try
        {
            // Warns by default, so two sales on account go through past the limit.
            (await client.PostAsJsonAsync(Invoices, Sale(masters, 1m))).StatusCode
                .ShouldBe(HttpStatusCode.Created);

            await SetAsync(client, new FirmPolicies(CreditLimit: CreditLimitPolicy.Block));

            // Owes 15 against 20: another 15 on account would take them to 30.
            HttpResponseMessage refused = await client.PostAsJsonAsync(Invoices, Sale(masters, 1m));
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());
            (await refused.Content.ReadAsStringAsync()).ShouldContain("credit limit");

            // Paid at the counter: nothing more on account, so nothing to refuse.
            Guid cash = (await client.GetFromJsonAsync<List<LedgerSummary>>("/api/v1/accounting/ledgers"))!
                .First(ledger => ledger.Kind == LedgerKind.Cash).LedgerId;

            Dictionary<string, object?> paid = Sale(masters, 1m);
            paid["PaymentMode"] = SalesPaymentMode.Cash;
            paid["PaidInFull"] = true;
            paid["ReceivedIntoLedgerId"] = cash;

            HttpResponseMessage accepted = await client.PostAsJsonAsync(Invoices, paid);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
        }
        finally
        {
            await SetAsync(client, FirmPolicies.Default);
        }
    }

    [Fact]
    public async Task Where_the_firm_chose_it_a_purchase_sets_every_products_cost_and_rebuilds_its_retail_rate()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string product = $"/api/v1/inventory/products/{masters.ProductId}";

        // Average-costed, with a 20% margin and a printed MRP of 55.
        HttpResponseMessage priced = await client.PutAsJsonAsync($"{product}/rates", new
        {
            CostingMethod = CostingMethod.AverageRate,
            Cost = 30m,
            RetailRate = 36m,
            ProfitPercentage = 20m,
            MaximumRetailPrice = 55m,
        });
        priced.StatusCode.ShouldBe(HttpStatusCode.NoContent, await priced.Content.ReadAsStringAsync());

        try
        {
            // Off by default: an average-costed product keeps its own figures.
            await BuyAsync(client, masters, quantity: 2m, rate: 40m);
            ProductDetail untouched = (await client.GetFromJsonAsync<ProductDetail>(product))!;
            untouched.Cost.ShouldBe(30m);
            untouched.RetailRate.ShouldBe(36m);

            await SetAsync(client, new FirmPolicies(PurchasesUpdateProductRates: true));

            await BuyAsync(client, masters, quantity: 2m, rate: 40m);
            ProductDetail repriced = (await client.GetFromJsonAsync<ProductDetail>(product))!;
            repriced.Cost.ShouldBe(40m);
            repriced.RetailRate.ShouldBe(48m);

            // Never above the MRP on the pack, whatever the margin says.
            await BuyAsync(client, masters, quantity: 2m, rate: 50m);
            ProductDetail capped = (await client.GetFromJsonAsync<ProductDetail>(product))!;
            capped.Cost.ShouldBe(50m);
            capped.RetailRate.ShouldBe(55m);
        }
        finally
        {
            await SetAsync(client, FirmPolicies.Default);
        }
    }

    private static Dictionary<string, object?> Sale(TestMasters masters, decimal quantity) =>
        new(StringComparer.Ordinal)
        {
            ["Date"] = Today,
            ["CustomerLedgerId"] = masters.CustomerId,
            ["WarehouseId"] = masters.WarehouseId,
            ["Kind"] = 1,
            ["Lines"] = new[] { new { masters.ProductId, Quantity = quantity, Rate = 15m, TaxPercentage = 0m } },
            ["Post"] = true,
        };

    private static async Task SetAsync(HttpClient client, FirmPolicies policies)
    {
        HttpResponseMessage saved = await client.PutAsJsonAsync(Policies, policies);
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<StockValuationRow> PositionAsync(HttpClient client, TestMasters masters) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
            $"/api/v1/inventory/stock/valuation?warehouseId={masters.WarehouseId}"))!
            .Rows.Single(row => row.ProductId == masters.ProductId);

    private static async Task BuyAsync(HttpClient client, TestMasters masters, decimal quantity, decimal rate)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"S-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = rate, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
