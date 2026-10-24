using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Inventory.Products;
using ERP.Application.Inventory.Stock;
using ERP.Application.Platform.Import;
using ERP.Application.Sales;

namespace ERP.Api.Tests;

/// <summary>A firm's data brought across from EasyRetail, through the real host.</summary>
/// <remarks>
/// <para>
/// A small export shaped like a real one - the RAJWA database these rules were worked out
/// against - with the awkward parts it has: built-in ledgers to map onto Inspire's own,
/// unused ones to leave behind, a "Primary" placeholder brand and category, a product held
/// twice in one warehouse under two batch names, a negative stock line, a duplicated
/// barcode, and opening balances that do not agree.
/// </para>
/// <para>
/// Identifiers are offset per run so the test can be repeated against the same database;
/// the built-in ledgers keep EasyRetail's own low numbers because that is how they are
/// recognised.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class EasyRetailImportEndpointTests
{
    private const string Import = "/api/v1/platform/import/easyretail";

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="EasyRetailImportEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public EasyRetailImportEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_export_is_imported_reconciled_and_can_be_imported_again()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        int o = Random.Shared.Next(1_000, 900_000);
        string tag = o.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Package package = Package.Build(o, tag);

        ImportStepResult masters = await PostAsync<ImportStepResult>(client, "masters", package.Masters);
        masters.Created.ShouldBeGreaterThan(0);

        // Built-in Cash and Sales land on Inspire's own; the unused built-in is left out.
        List<LedgerSummary> ledgers = (await client.GetFromJsonAsync<List<LedgerSummary>>("/api/v1/accounting/ledgers"))!;
        ledgers.Count(l => l.Code == "CASH").ShouldBe(1);
        ledgers.ShouldNotContain(l => l.Name == "EducationalCess");
        ledgers.ShouldContain(l => l.Name == $"SHOP RENT {tag}" && l.GroupCode == "5900");
        ledgers.ShouldContain(l => l.Name == $"QNB {tag}" && l.Kind == Domain.Accounting.LedgerKind.Bank);

        List<CustomerResponse> customers = (await client.GetFromJsonAsync<List<CustomerResponse>>(
            "/api/v1/sales/customers?activeOnly=false"))!;
        CustomerResponse customer = customers.Single(c => c.Name == $"Al Noor {tag}");
        customer.Code.ShouldBe($"CUS-{o + 1:D4}");
        customer.Contact.MobileNumber.ShouldBe("55512345");
        customer.Terms.CreditDays.ShouldBe(30);

        // Where they are, matched to the firm's own lists: QATAR is Qatar, DOHA its
        // state, a new area is added, and EasyRetail's "Primary" route is no route.
        customer.Profile.CountryCode.ShouldBe("QA");
        customer.TaxDetails.StateCode.ShouldBe("QA-DA");
        customer.Profile.City.ShouldBe("AL WAKRA");
        customer.Profile.ContactPerson.ShouldBe("SALIM");
        customer.Profile.TinNumber.ShouldBe("T-1");
        customer.Profile.AreaId.ShouldNotBeNull();
        customer.Profile.RouteId.ShouldBeNull();
        customer.Profile.SendsSms.ShouldBeTrue();

        ImportStepResult products = await PostAsync<ImportStepResult>(client, "products", package.Products);
        products.Created.ShouldBe(2);
        products.Warnings.ShouldContain(w => w.Contains("barcodes were left out", StringComparison.Ordinal));

        List<ProductSummary> listed = (await client.GetFromJsonAsync<List<ProductSummary>>(
            $"/api/v1/inventory/products?includeInactive=true&search=IP{tag}"))!;
        ProductSummary phone = listed.Single(p => p.Code == $"IP{tag}");
        phone.CategoryName.ShouldBe($"PHONES {tag}");

        ImportBalancesResult balances = await PostAsync<ImportBalancesResult>(client, "balances", package.Balances);
        balances.TotalDebit.ShouldBe(balances.TotalCredit);
        // Rent 200 debit and sales 500 credit carry to equity as a net 300 credit.
        balances.ProfitCarried.ShouldBe(-300m);
        // The export's balances are out by 25 on the debit side.
        balances.Difference.ShouldBe(25m);

        ImportStepResult stock = await PostAsync<ImportStepResult>(client, "stock", package.Stock);
        // Two batch rows of one product become one line; the negative row is left out.
        stock.Created.ShouldBe(1);
        stock.Warnings.ShouldContain(w => w.Contains("zero or below", StringComparison.Ordinal));

        StockValuationReport valuation = (await client.GetFromJsonAsync<StockValuationReport>(
            "/api/v1/inventory/stock/valuation"))!;
        StockValuationRow held = valuation.Rows.Single(r => r.ProductCode == $"IP{tag}");
        held.Quantity.ShouldBe(5m);
        held.Value.ShouldBe(5m * 2400m);

        // Again: everything is recognised and nothing is duplicated.
        ImportStepResult again = await PostAsync<ImportStepResult>(client, "masters", package.Masters);
        again.Created.ShouldBe(0);
        again.Matched.ShouldBe(0);
        again.AlreadyImported.ShouldBe(masters.Created + masters.Matched);
        ImportStepResult productsAgain = await PostAsync<ImportStepResult>(client, "products", package.Products);
        productsAgain.Created.ShouldBe(0);
        productsAgain.AlreadyImported.ShouldBe(2);
        ImportStepResult stockAgain = await PostAsync<ImportStepResult>(client, "stock", package.Stock);
        stockAgain.Created.ShouldBe(0);
        // Both batch rows of the phone are recognised, though they became one line.
        stockAgain.AlreadyImported.ShouldBe(2);
        stockAgain.Skipped.ShouldBe(1);
        (await client.GetFromJsonAsync<List<CustomerResponse>>("/api/v1/sales/customers?activeOnly=false"))!
            .Count(c => c.Name == $"Al Noor {tag}").ShouldBe(1);

        // Put the shared ledgers' opening balances back, for the tests that follow.
        await PostAsync<ImportBalancesResult>(client, "balances", package.ZeroBalances);
    }

    [Fact]
    public async Task The_exporter_is_downloaded_where_the_server_has_it_and_refused_where_it_does_not()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        // Where the image put it: beside the server, under tools/.
        string tools = Path.Combine(AppContext.BaseDirectory, "tools");
        string exporter = Path.Combine(tools, "easyretail-export.exe");

        if (File.Exists(exporter))
        {
            File.Delete(exporter);
        }

        // A server built without it says so, rather than failing.
        HttpResponseMessage missing = await client.GetAsync($"{Import}/exporter");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).ShouldContain("Import.ExporterUnavailable");

        byte[] program = [(byte)'M', (byte)'Z', 1, 2, 3];
        Directory.CreateDirectory(tools);
        await File.WriteAllBytesAsync(exporter, program);

        try
        {
            HttpResponseMessage response = await client.GetAsync($"{Import}/exporter");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("easyretail-export.exe");
            (await response.Content.ReadAsByteArrayAsync()).ShouldBe(program);
        }
        finally
        {
            File.Delete(exporter);
        }
    }

    [Fact]
    public async Task An_unreachable_database_is_reported_rather_than_thrown()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync($"{Import}/read", new
        {
            Server = "127.0.0.1,1",
            Database = "EasyRetail2025",
            UserName = "sa",
            Password = "wrong",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("could not be read");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string step, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"{Import}/{step}", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record Package(object Masters, object Products, object Balances, object ZeroBalances, object Stock)
    {
        public static Package Build(int o, string tag)
        {
            ErGroup[] groups =
            [
                new(1, "Asset", 0), new(2, "Liability", 0), new(3, "Income", 0), new(4, "Expense", 0),
                new(7, "Current Assets", 1), new(10, "Capital Account", 2), new(13, "Current Liabilities & Provisions", 2),
                new(14, "Direct Income", 3), new(17, "Indirect Expense", 4), new(19, "Bank Accounts", 7),
                new(24, "Sundry Creditors", 13), new(25, "Sundry Debtors", 7),
            ];

            ErLedger[] ledgers =
            [
                new(1, "Cash", 7, 0m, 1000m, 3),
                new(3, "Sales", 14, 0m, -500m, 2),
                new(21, "EducationalCess", 13, 0m, 0m, 0),
                new(o + 10, $"QNB {tag}", 19, 0m, -150m, 1),
                new(o + 11, $"SHOP RENT {tag}", 17, 0m, 200m, 1),
                new(o + 12, $"CAPITAL {tag}", 10, -500m, -500m, 0),
                new(o + 20, $"Al Noor {tag}", 25, 0m, 300m, 2),
                new(o + 21, $"Gulf Mobiles {tag}", 24, 0m, -325m, 1),
            ];

            ErProduct Product(int id, string code, string name, int subClass, decimal cost, decimal retail, decimal mrp, string upc) =>
                new(id, code, name, string.Empty, o + 2, subClass, o + 1, o + 1, o + 1, o + 1, 1,
                    cost, retail, 0m, 0m, mrp, 0m, 0m, 2m, 0m, string.Empty, string.Empty, upc, string.Empty,
                    string.Empty, string.Empty, string.Empty, "Primary", string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, true);

            ErProduct phone = Product(o + 100, $"IP{tag}", $"IPHONE 15 {tag}", o + 50, 2400m, 2999m, 0m, $"U{tag}1");
            ErProduct charger = Product(o + 101, $"CH{tag}", $"CHARGER {tag}", o + 51, 40m, 79m, 60m, $"U{tag}2");

            ErStock[] stock =
            [
                new(o + 100, o + 1, "Primary", 3m, 2400m),
                new(o + 100, o + 1, string.Empty, 2m, 2400m),
                new(o + 101, o + 1, "Primary", -4m, 40m),
            ];

            return new Package(
                Masters: new
                {
                    Groups = groups,
                    Ledgers = ledgers,
                    Customers = new[]
                    {
                        new ErParty(o + 1, o + 20, string.Empty, $"Al Noor {tag}", "Shop 4", "Doha", "55512345", string.Empty, string.Empty, string.Empty, 5000m, 30)
                        {
                            City = "AL WAKRA",
                            State = "DOHA",
                            Country = "QATAR",
                            PinCode = "1234",
                            ContactPerson = "SALIM",
                            Tin = "T-1",
                            Area = $"SOUQ {tag}",
                            Route = "Primary",
                            IsSms = 1,
                        },
                    },
                    Suppliers = new[]
                    {
                        new ErParty(o + 2, o + 21, string.Empty, $"Gulf Mobiles {tag}", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0m, 0),
                    },
                    Employees = Array.Empty<ErEmployee>(),
                    Categories = new[] { new ErCategory(o + 3, "Primary", 0), new ErCategory(o + 2, $"PHONE {tag}", o + 3) },
                    Brands = new[] { new ErNamed(o + 4, "Primary") },
                    Units = new[] { new ErUnit(o + 1, $"No {tag}", $"N{o % 1000}", 0, 1m) },
                    Warehouses = new[] { new ErNamed(o + 1, "Primary") },
                },
                Products: new
                {
                    Products = new[] { phone, charger },
                    Barcodes = new[]
                    {
                        new ErBarcode(o + 100, $"U{tag}1", 0m, 0m, 0m, 0m),
                        new ErBarcode(o + 101, $"U{tag}1", 0m, 0m, 0m, 0m),
                    },
                    SubClasses = new[] { new ErNamed(o + 50, $"PHONES {tag}"), new ErNamed(o + 51, "Primary") },
                },
                Balances: new { Ledgers = ledgers, CarryProfitToEquity = true },
                ZeroBalances: new
                {
                    Ledgers = ledgers.Select(l => l with { Balance = 0m }).ToArray(),
                    CarryProfitToEquity = true,
                },
                Stock: new { AsAt = DateOnly.FromDateTime(DateTime.UtcNow), Rows = stock });
        }
    }
}
