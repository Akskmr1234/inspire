using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Stock;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;

namespace ERP.Api.Tests;

/// <summary>Tests the VAT and GST returns of §7.3, end to end.</summary>
/// <remarks>
/// The figures a firm files with a state, read through the same endpoints an accountant
/// would. What is proved here is the whole chain rather than the arithmetic alone: a sale
/// entered and posted, then the return afterwards stating the supply and the tax the
/// document actually charged.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class TaxReturnEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Invoices = "/api/v1/sales/invoices";
    private const string Reports = "/api/v1/accounting/reports";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public TaxReturnEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_posted_sale_reaches_the_output_tax_return()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        OutputTaxReport before = await OutputAsync(client);

        await SellAsync(client, quantity: 4m, rate: 50m, taxPercentage: 5m);

        OutputTaxReport after = await OutputAsync(client);

        // The seeded firm is a VAT firm, so one head and no GST anywhere in sight.
        after.Regime.ShouldBe(TaxRegime.GccVat);
        after.TaxableSupplies.ShouldBe(before.TaxableSupplies + 200m);

        decimal chargedBefore = before.Totals
            .Where(total => total.Component == TaxComponentType.Vat)
            .Sum(total => total.TaxAmount);

        after.Totals
            .Single(total => total.Component == TaxComponentType.Vat)
            .TaxAmount.ShouldBe(chargedBefore + 10m);

        after.Rows.ShouldContain(row => row.Component == TaxComponentType.Vat);
    }

    [Fact]
    public async Task The_summary_states_what_is_payable_and_whether_it_reconciles()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        await SellAsync(client, quantity: 2m, rate: 100m, taxPercentage: 5m);

        TaxSummaryReport summary = await client.GetFromJsonAsync<TaxSummaryReport>(
            $"{Reports}/tax-summary?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")
            ?? throw new InvalidOperationException("No summary.");

        TaxSummaryLine line = summary.Lines.Single(l => l.Component == TaxComponentType.Vat);

        // Output less input, and the sale's own journal put the same figure on the
        // ledger - so the two sides agree and the return is reconciled.
        line.NetPayable.ShouldBe(line.OutputTax - line.InputTax);
        line.Difference.ShouldBe(0m);
        summary.IsReconciled.ShouldBeTrue();
    }

    [Fact]
    public async Task The_input_side_is_empty_until_something_posts_to_it()
    {
        // Nothing produces input tax yet but a journal somebody writes: no purchase
        // module exists. Stated plainly rather than filled with a derived figure.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        InputTaxReport report = await client.GetFromJsonAsync<InputTaxReport>(
            $"{Reports}/input-tax?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")
            ?? throw new InvalidOperationException("No report.");

        report.Regime.ShouldBe(TaxRegime.GccVat);
        report.Rows.ShouldBeEmpty();
        report.Totals.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_period_that_ends_before_it_starts_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"{Reports}/output-tax?from={Today:yyyy-MM-dd}&to={Today.AddDays(-5):yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_returns_refuse_an_anonymous_caller()
    {
        HttpClient client = _factory.CreateAnonymousClient();

        foreach (string report in new[] { "output-tax", "input-tax", "tax-summary" })
        {
            (await client.GetAsync(
                $"{Reports}/{report}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    private static async Task<OutputTaxReport> OutputAsync(HttpClient client) =>
        await client.GetFromJsonAsync<OutputTaxReport>(
            $"{Reports}/output-tax?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")
        ?? throw new InvalidOperationException("No report.");

    /// <summary>Sells something, so there is a supply for the return to state.</summary>
    private static async Task SellAsync(
        HttpClient client,
        decimal quantity,
        decimal rate,
        decimal taxPercentage)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateAsync(
            client, "categories", new { Code = $"TCAT{suffix}", Name = $"Tax {suffix}" });

        Guid unitId = await CreateAsync(
            client, "units", new { Code = $"TEA{suffix}", Name = "Each" });

        Guid warehouseId = await CreateAsync(
            client, "warehouses", new { Code = $"TWH{suffix}", Name = "Tax store" });

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new
            {
                Code = $"TAX{suffix}",
                Description = "A thing",
                CategoryId = categoryId,
                StockUnitId = unitId,
                ItemType = 1,
            });

        product.StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage received = await client.PostAsJsonAsync(
            $"{Inventory}/stock/documents",
            new
            {
                Type = (int)StockDocumentType.MaterialReceipt,
                Date = Today,
                WarehouseId = warehouseId,
                Lines = new[] { new { ProductId = productId, Quantity = 100m, Rate = 10m } },
            });

        received.StatusCode.ShouldBe(HttpStatusCode.Created);

        Guid customerId = await CustomerEndpointTests.CreateAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Invoices,
            new
            {
                Date = Today,
                CustomerLedgerId = customerId,
                WarehouseId = warehouseId,
                Lines = new[]
                {
                    new
                    {
                        ProductId = productId,
                        Quantity = quantity,
                        Rate = rate,
                        TaxPercentage = taxPercentage,
                    },
                },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        SalesInvoiceResponse draft =
            (await created.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Invoices}/{draft.SalesInvoiceId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string resource, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }
}
