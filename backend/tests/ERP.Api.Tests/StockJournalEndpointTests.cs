using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Stock;
using ERP.Domain.Inventory;

namespace ERP.Api.Tests;

/// <summary>Tests that stock movements reach the nominal ledger, end to end.</summary>
/// <remarks>
/// The answer to open question 8a, verified where it matters: not that a journal object
/// was constructed, but that after posting a stock document the trial balance says what
/// the stock valuation says. These read the accounts through the same report an
/// accountant would.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class StockJournalEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Stock = $"{Inventory}/stock";
    private const string Reports = "/api/v1/accounting/reports";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public StockJournalEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_receipt_debits_inventory_and_credits_the_counter_account()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        JournalFixtures fixtures = await JournalFixtures.CreateAsync(client);

        decimal stockBefore = await ClosingDebitAsync(client, "STOCK");
        decimal consumedBefore = await ClosingCreditAsync(client, "CONSUMPTION");

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        // Goods worth 250 came in: stock is worth 250 more, and the account they came
        // back from is 250 lighter.
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockBefore + 250m);
        (await ClosingCreditAsync(client, "CONSUMPTION")).ShouldBe(consumedBefore + 250m);
    }

    [Fact]
    public async Task An_issue_credits_inventory_at_what_the_goods_were_valued_at()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        JournalFixtures fixtures = await JournalFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        decimal stockAfterReceipt = await ClosingDebitAsync(client, "STOCK");

        await PostAsync(
            client, StockDocumentType.MaterialIssue, fixtures.MainId,
            [Line(fixtures.ProductId, 4m)]);

        // Four at the average of 25, which the line never carried: the journal is built
        // from what the posting did, not from what somebody typed.
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockAfterReceipt - 100m);
    }

    [Fact]
    public async Task Damaged_stock_goes_to_the_loss_account_rather_than_to_consumption()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        JournalFixtures fixtures = await JournalFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        decimal lossBefore = await ClosingDebitAsync(client, "STOCK-LOSS");

        await PostAsync(
            client, StockDocumentType.DamagedStock, fixtures.MainId,
            [Line(fixtures.ProductId, 2m)]);

        (await ClosingDebitAsync(client, "STOCK-LOSS")).ShouldBe(lossBefore + 50m);
    }

    [Fact]
    public async Task A_transfer_says_nothing_to_the_accounts()
    {
        // The goods changed shelves. They did not change hands and they did not change
        // value, so there is nothing for the books to record.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        JournalFixtures fixtures = await JournalFixtures.CreateAsync(client);

        await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        decimal before = await ClosingDebitAsync(client, "STOCK");

        await PostAsync(
            client, StockDocumentType.StockTransfer, fixtures.MainId,
            [Line(fixtures.ProductId, 4m)],
            destination: fixtures.ShopId);

        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(before);
    }

    [Fact]
    public async Task Cancelling_a_document_withdraws_the_journal_it_raised()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        JournalFixtures fixtures = await JournalFixtures.CreateAsync(client);

        decimal before = await ClosingDebitAsync(client, "STOCK");

        CreateStockDocumentResponse receipt = await PostAsync(
            client, StockDocumentType.MaterialReceipt, fixtures.MainId,
            [Line(fixtures.ProductId, 10m, rate: 25m)]);

        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(before + 250m);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Stock}/documents/{receipt.StockDocumentId}/cancel",
            new { Reason = "Received against the wrong godown" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The journal is cancelled, so it leaves the balances exactly as it found them.
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(before);
    }

    // ------------------------------------------------------------------ helpers

    private static object Line(Guid productId, decimal quantity, decimal rate = 0m) =>
        new { ProductId = productId, Quantity = quantity, Rate = rate };

    private static async Task<CreateStockDocumentResponse> PostAsync(
        HttpClient client,
        StockDocumentType type,
        Guid warehouseId,
        object[] lines,
        Guid? destination = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Stock}/documents",
            new
            {
                Type = (int)type,
                Date = Today,
                WarehouseId = warehouseId,
                DestinationWarehouseId = destination,
                Lines = lines,
                PostImmediately = true,
            });

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateStockDocumentResponse>())!;
    }

    private static async Task<TrialBalanceRow?> RowAsync(HttpClient client, string ledgerCode)
    {
        DateOnly from = new(Today.Year, 1, 1);

        TrialBalanceResponse report = (await client.GetFromJsonAsync<TrialBalanceResponse>(
            $"{Reports}/trial-balance?from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))!;

        return report.Rows.FirstOrDefault(row => row.LedgerCode == ledgerCode);
    }

    private static async Task<decimal> ClosingDebitAsync(HttpClient client, string ledgerCode) =>
        (await RowAsync(client, ledgerCode))?.ClosingDebit ?? 0m;

    private static async Task<decimal> ClosingCreditAsync(HttpClient client, string ledgerCode) =>
        (await RowAsync(client, ledgerCode))?.ClosingCredit ?? 0m;

    /// <summary>The masters a stock document needs, on a product nobody else touches.</summary>
    /// <param name="MainId">A warehouse.</param>
    /// <param name="ShopId">A second one, so transfers are testable.</param>
    /// <param name="ProductId">A stocked product.</param>
    private sealed record JournalFixtures(Guid MainId, Guid ShopId, Guid ProductId)
    {
        internal static async Task<JournalFixtures> CreateAsync(HttpClient client)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

            Guid categoryId = await CreateMasterAsync(
                client, "categories", new { Code = $"JCAT{suffix}", Name = $"Journal {suffix}" });

            Guid eachId = await CreateMasterAsync(
                client, "units", new { Code = $"JEA{suffix}", Name = "Each" });

            Guid mainId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"JMAIN{suffix}", Name = "Main store" });

            Guid shopId = await CreateMasterAsync(
                client, "warehouses", new { Code = $"JSHOP{suffix}", Name = "Shop floor" });

            HttpResponseMessage created = await client.PostAsJsonAsync(
                $"{Inventory}/products",
                new
                {
                    Code = $"JRN{suffix}",
                    Description = "A thing",
                    CategoryId = categoryId,
                    StockUnitId = eachId,
                    ItemType = 1,
                });

            created.StatusCode.ShouldBe(HttpStatusCode.Created);

            return new JournalFixtures(
                mainId, shopId, await created.Content.ReadFromJsonAsync<Guid>());
        }

        private static async Task<Guid> CreateMasterAsync(
            HttpClient client,
            string resource,
            object body)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"{Inventory}/{resource}", body);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);

            return await response.Content.ReadFromJsonAsync<Guid>();
        }
    }
}
