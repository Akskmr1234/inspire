using System.Net;
using System.Net.Http.Json;
using ERP.Application.Abstractions;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>
/// What the sales counter records beside the goods: how the sale was paid, who made it
/// and who billed it, a charge nobody had set up yet, and a sale saved and posted in one
/// step.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SalesCounterEndpointTests
{
    private const string Ledgers = "/api/v1/accounting/ledgers";
    private const string Groups = "/api/v1/accounting/account-groups";
    private const string Invoices = "/api/v1/sales/invoices";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="SalesCounterEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public SalesCounterEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_sale_records_its_payment_mode_salesman_and_billing_man_and_posts_in_one_step()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Guid expenses = await GroupAsync(client, "5900");

        LedgerDetail salesman = await LedgerAsync(client, $"Salesman {tag}", expenses, LedgerKind.Employee);
        LedgerDetail billingMan = await LedgerAsync(client, $"Biller {tag}", expenses, LedgerKind.Employee);
        LedgerDetail rent = await LedgerAsync(client, $"Rent {tag}", expenses, LedgerKind.General);

        await BuyAsync(client, masters);

        // Somebody who is not an employee is refused as a salesman, and nothing is saved.
        HttpResponseMessage stranger = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            SalesmanLedgerId = rent.LedgerId,
            Post = true,
        }));
        stranger.StatusCode.ShouldBe(HttpStatusCode.NotFound, await stranger.Content.ReadAsStringAsync());

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            SalesmanLedgerId = salesman.LedgerId,
            BillingManLedgerId = billingMan.LedgerId,
            Post = true,
        }));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        // Saved and posted together: no draft is left to post separately.
        invoice.Status.ShouldBe(SalesInvoiceStatus.Posted);

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"{Invoices}/{invoice.SalesInvoiceId}"))!;
        detail.PaymentMode.ShouldBe(SalesPaymentMode.Cash);
        detail.SalesmanLedgerId.ShouldBe(salesman.LedgerId);
        detail.BillingManLedgerId.ShouldBe(billingMan.LedgerId);
        detail.BillId.ShouldNotBeNull();

        PagedResult<SalesInvoiceSummary> page = (await client.GetFromJsonAsync<PagedResult<SalesInvoiceSummary>>(
            $"{Invoices}?customerLedgerId={masters.CustomerId}"))!;
        SalesInvoiceSummary row = page.Items.Single(item => item.SalesInvoiceId == invoice.SalesInvoiceId);
        row.PaymentMode.ShouldBe(SalesPaymentMode.Cash);
        row.SalesmanName.ShouldBe(salesman.Name);
        row.BillingManName.ShouldBe(billingMan.Name);
        row.CreatedAtUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        // A sale the books refuse - more than is on the shelf - leaves no draft behind.
        int before = page.TotalCount;
        HttpResponseMessage refused = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 500m, Rate = 15m, TaxPercentage = 0m } },
            Post = true,
        });
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await refused.Content.ReadAsStringAsync());

        (await client.GetFromJsonAsync<PagedResult<SalesInvoiceSummary>>(
            $"{Invoices}?customerLedgerId={masters.CustomerId}"))!.TotalCount.ShouldBe(before);
    }

    [Fact]
    public async Task Any_expense_ledger_can_be_set_up_as_an_additional_ledger_and_used_on_a_sale()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Guid expenses = await GroupAsync(client, "5900");
        LedgerDetail cleaning = await LedgerAsync(client, $"Cleaning {tag}", expenses, LedgerKind.General);

        await BuyAsync(client, masters);

        // Not set up yet, so a sale carrying it is refused.
        HttpResponseMessage unmapped = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            Charges = new[] { new { cleaning.LedgerId, Amount = 4m } },
        }));
        unmapped.StatusCode.ShouldBe(HttpStatusCode.NotFound, await unmapped.Content.ReadAsStringAsync());

        HttpResponseMessage mapped = await client.PostAsJsonAsync("/api/v1/accounting/charges", new
        {
            Document = ChargeableDocument.Sales,
            cleaning.LedgerId,
            IsAddition = true,
        });
        mapped.StatusCode.ShouldBe(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        ChargeSetup charge = (await mapped.Content.ReadFromJsonAsync<ChargeSetup>())!;
        charge.LedgerId.ShouldBe(cleaning.LedgerId);
        charge.IsAddition.ShouldBeTrue();

        // Set up again the other way round: the first answer stands.
        ChargeSetup again = (await (await client.PostAsJsonAsync("/api/v1/accounting/charges", new
        {
            Document = ChargeableDocument.Sales,
            cleaning.LedgerId,
            IsAddition = false,
        })).Content.ReadFromJsonAsync<ChargeSetup>())!;
        again.IsAddition.ShouldBeTrue();

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            Charges = new[] { new { cleaning.LedgerId, Amount = 4m } },
        }));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.ChargeTotal.ShouldBe(4m);

        // A customer is not a charge.
        HttpResponseMessage party = await client.PostAsJsonAsync("/api/v1/accounting/charges", new
        {
            Document = ChargeableDocument.Sales,
            LedgerId = masters.CustomerId,
            IsAddition = true,
        });
        party.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await party.Content.ReadAsStringAsync());

        // Taken off the sales setup in Settings, it is no longer offered or accepted.
        HttpResponseMessage withdrawn = await client.DeleteAsync(
            $"/api/v1/accounting/charges/{ChargeableDocument.Sales}/{cleaning.LedgerId}");
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());
        (await client.GetFromJsonAsync<List<ChargeSetup>>("/api/v1/accounting/charges?document=Sales"))!
            .ShouldNotContain(row => row.LedgerId == cleaning.LedgerId);
        (await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            Charges = new[] { new { cleaning.LedgerId, Amount = 4m } },
        }))).IsSuccessStatusCode.ShouldBeFalse();

        // Never set up for a purchase, there is nothing there to take off.
        (await client.DeleteAsync(
            $"/api/v1/accounting/charges/{ChargeableDocument.Purchase}/{cleaning.LedgerId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_units_conversion_factor_can_change_until_something_is_recorded_in_it()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/inventory/units", new
        {
            Code = $"BX{tag}",
            Name = "Box",
            BaseUnitId = masters.UnitId,
            ConversionFactor = 10m,
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid box = await created.Content.ReadFromJsonAsync<Guid>();

        // Unused: everything may change.
        HttpResponseMessage revised = await client.PutAsJsonAsync($"/api/v1/inventory/units/{box}", new
        {
            Name = "Box of 12",
            Symbol = "bx",
            DecimalPlaces = 0,
            ConversionFactor = 12m,
        });
        revised.StatusCode.ShouldBe(HttpStatusCode.NoContent, await revised.Content.ReadAsStringAsync());

        // The product's own stock unit is in use, so its factor is fixed - and a base
        // unit's is one in any case.
        HttpResponseMessage baseFactor = await client.PutAsJsonAsync(
            $"/api/v1/inventory/units/{masters.UnitId}",
            new { Name = $"Each {masters.UnitId:N}", DecimalPlaces = 2, ConversionFactor = 1m });
        baseFactor.StatusCode.ShouldBe(HttpStatusCode.NoContent, await baseFactor.Content.ReadAsStringAsync());

        // Bought in boxes: now the box is in use and its factor is fixed, while its name
        // can still change.
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"U-{tag}",
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 120m, TaxPercentage = 0m, UnitId = box } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());

        HttpResponseMessage locked = await client.PutAsJsonAsync($"/api/v1/inventory/units/{box}", new
        {
            Name = "Box of 12",
            DecimalPlaces = 0,
            ConversionFactor = 24m,
        });
        locked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await locked.Content.ReadAsStringAsync());

        HttpResponseMessage renamed = await client.PutAsJsonAsync($"/api/v1/inventory/units/{box}", new
        {
            Name = "Carton",
            DecimalPlaces = 0,
            ConversionFactor = 12m,
        });
        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await renamed.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, object?> Sale(TestMasters masters, object extra)
    {
        Dictionary<string, object?> body = new(StringComparer.Ordinal)
        {
            ["Date"] = Today,
            ["CustomerLedgerId"] = masters.CustomerId,
            ["WarehouseId"] = masters.WarehouseId,
            ["Kind"] = 1,
            ["Lines"] = new[] { new { masters.ProductId, Quantity = 1m, Rate = 15m, TaxPercentage = 0m } },
        };

        foreach (System.Reflection.PropertyInfo property in extra.GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(extra);
        }

        return body;
    }

    private static async Task<Guid> GroupAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<AccountGroupSummary>>(Groups))!
            .Single(group => group.Code == code)
            .AccountGroupId;

    private static async Task<LedgerDetail> LedgerAsync(
        HttpClient client, string name, Guid groupId, LedgerKind kind)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(Ledgers, new
        {
            Name = name,
            AccountGroupId = groupId,
            Kind = kind,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<LedgerDetail>())!;
    }

    private static async Task BuyAsync(HttpClient client, TestMasters masters)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"S-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
