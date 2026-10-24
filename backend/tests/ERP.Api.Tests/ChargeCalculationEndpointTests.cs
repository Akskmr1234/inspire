using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;

namespace ERP.Api.Tests;

/// <summary>How charges - the additional ledgers - add up on an invoice and reach the books.</summary>
/// <remarks>
/// The firm was unsure of the calculation. This pins it down with figures: charges come
/// after tax and are not taxed, each adds or deducts as the firm's setup says (not as the
/// screen once let somebody choose), each line is taxed on its value rounded to the currency, and the journal
/// carries every part to its own account.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ChargeCalculationEndpointTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public ChargeCalculationEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_charge_is_marked_as_a_default_and_unmarked_again()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        const string Setup = "/api/v1/accounting/charges";

        IReadOnlyList<ChargeSetup> sales = (await client.GetFromJsonAsync<IReadOnlyList<ChargeSetup>>(
            $"{Setup}?document=PurchaseOrder"))!;
        ChargeSetup freight = sales.Single(charge => charge.Code == "FREIGHT");

        foreach (bool isDefault in new[] { true, false })
        {
            HttpResponseMessage marked = await client.PutAsJsonAsync(
                $"{Setup}/PurchaseOrder/{freight.LedgerId}/default", new { IsDefault = isDefault });
            marked.StatusCode.ShouldBe(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync());
            (await marked.Content.ReadFromJsonAsync<ChargeSetup>())!.IsDefault.ShouldBe(isDefault);

            (await client.GetFromJsonAsync<IReadOnlyList<ChargeSetup>>($"{Setup}?document=PurchaseOrder"))!
                .Single(charge => charge.LedgerId == freight.LedgerId).IsDefault.ShouldBe(isDefault);
        }

        // A ledger that is not set up for the document cannot be made one of its defaults.
        (await client.PutAsJsonAsync($"{Setup}/PurchaseOrder/{Guid.NewGuid()}/default", new { IsDefault = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Charges_add_or_deduct_after_tax_and_are_never_taxed()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        List<ChargeSetup> charges = (await client.GetFromJsonAsync<List<ChargeSetup>>(
            "/api/v1/accounting/charges?document=Sales"))!;

        ChargeSetup freight = charges.Single(charge => charge.Code == "FREIGHT");
        ChargeSetup discount = charges.Single(charge => charge.Code == "DISC-ALLOWED");
        freight.IsAddition.ShouldBeTrue();
        discount.IsAddition.ShouldBeFalse();
        charges.Single(charge => charge.Code == "ROUND-OFF").IsComputed.ShouldBeTrue();

        await BuyAsync(client, masters);

        // 3 × 33.335 = 100.005, taxed as 100.01 (the line rounded to the currency - a line
        // like this one used to be refused outright); 5% of it is 5.0005, so 5.00 tax.
        // Then the charges, untaxed: freight +20, discount −7.5. 100.01 + 5.00 + 20 − 7.5
        // = 117.51, with nothing left for round off.
        HttpResponseMessage sold = await client.PostAsJsonAsync("/api/v1/sales/invoices", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            Mode = TaxMode.Tax,
            Lines = new[] { new { masters.ProductId, Quantity = 3m, Rate = 33.335m, TaxPercentage = 5m } },
            Charges = new[]
            {
                new { freight.LedgerId, Amount = 20m },
                new { discount.LedgerId, Amount = 7.5m },
            },
        });

        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        invoice.Taxable.ShouldBe(100.01m);
        invoice.Tax.ShouldBe(5.00m);
        invoice.ChargeTotal.ShouldBe(12.5m);
        invoice.Total.ShouldBe(117.51m);
        invoice.RoundingDifference.ShouldBe(0m);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        PostSalesInvoiceResponse result = (await posted.Content.ReadFromJsonAsync<PostSalesInvoiceResponse>())!;

        // The journal balances, and each part sits on its own account.
        VoucherDetailResponse journal = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"/api/v1/accounting/vouchers/{result.JournalVoucherId}"))!;

        journal.TotalDebit.ShouldBe(journal.TotalCredit);
        journal.Lines.Single(line => line.LedgerId == masters.CustomerId).DebitAmount.ShouldBe(117.51m);
        journal.Lines.Single(line => line.LedgerId == freight.LedgerId).CreditAmount.ShouldBe(20m);
        journal.Lines.Single(line => line.LedgerId == discount.LedgerId).DebitAmount.ShouldBe(7.5m);
    }

    private static async Task BuyAsync(HttpClient client, TestMasters masters)
    {
        HttpResponseMessage bought = await client.PostAsJsonAsync("/api/v1/purchase/invoices", new
        {
            Date = Today,
            SupplierLedgerId = masters.SupplierId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            SupplierInvoiceNumber = $"C-{Guid.NewGuid():N}"[..12],
            Lines = new[] { new { masters.ProductId, Quantity = 10m, Rate = 10m, TaxPercentage = 0m } },
        });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid id = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{id}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
