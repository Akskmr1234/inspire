using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Reports;
using ERP.Application.Inventory.Stock;
using ERP.Application.Purchase;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;

namespace ERP.Api.Tests;

/// <summary>Tests the purchase endpoints end to end.</summary>
/// <remarks>
/// The whole buying flow through the real host: a supplier is created, a purchase is
/// entered, and posting it receives the stock, raises the debt and moves the books. What
/// is checked at the end is the trial balance - and in particular that Goods Received
/// nets to nothing once both halves of the model have landed, because that is the claim
/// the clearing account is there to make.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class PurchaseEndpointTests
{
    private const string Inventory = "/api/v1/inventory";
    private const string Purchases = "/api/v1/purchase/invoices";
    private const string Suppliers = "/api/v1/purchase/suppliers";
    private const string Reports = "/api/v1/accounting/reports";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public PurchaseEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_supplier_is_created_and_found_again()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Suppliers,
            new
            {
                Code = $"SUP{suffix}",
                Name = $"Gulf Wholesale {suffix}",
                Terms = new { CreditDays = 30 },
                TaxDetails = new { RegistrationNumber = "VAT-991" },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        SupplierResponse supplier =
            (await created.Content.ReadFromJsonAsync<SupplierResponse>())!;

        supplier.Terms.CreditDays.ShouldBe(30);
        supplier.IsActive.ShouldBeTrue();

        IReadOnlyList<SupplierResponse> found =
            (await client.GetFromJsonAsync<IReadOnlyList<SupplierResponse>>(
                $"{Suppliers}?search={suffix}&activeOnly=true"))!;

        found.ShouldHaveSingleItem().SupplierId.ShouldBe(supplier.SupplierId);
    }

    [Fact]
    public async Task A_supplier_code_already_in_use_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        object body = new { Code = $"DUP{suffix}", Name = "Twice" };

        (await client.PostAsJsonAsync(Suppliers, body))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync(Suppliers, body))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_customer_cannot_be_read_through_the_supplier_endpoint()
    {
        // The kind is checked as well as the firm. Without it a customer reached here
        // would be given supplier terms and would then appear in a supplier picker.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();

        Guid customerId = await CustomerEndpointTests.CreateAsync(client);

        (await client.GetAsync($"{Suppliers}/{customerId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_purchase_is_entered_as_a_draft_and_read_back_in_full()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Purchases, Purchase(fixtures, quantity: 4m, rate: 25m, taxPercentage: 5m));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        PurchaseInvoiceResponse draft =
            (await created.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!;

        draft.Status.ShouldBe(PurchaseInvoiceStatus.Draft);
        draft.Taxable.ShouldBe(100m);
        draft.Tax.ShouldBe(5m);
        draft.Total.ShouldBe(105m);

        PurchaseInvoiceDetail detail =
            (await client.GetFromJsonAsync<PurchaseInvoiceDetail>(
                $"{Purchases}/{draft.PurchaseInvoiceId}"))!;

        detail.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(4m);
        detail.Lines[0].Components.ShouldHaveSingleItem().Amount.ShouldBe(5m);
        detail.SupplierInvoiceNumber.ShouldNotBeNull();

        // A draft has produced nothing yet, and says so.
        detail.StockDocumentId.ShouldBeNull();
        detail.BillId.ShouldBeNull();
        detail.JournalVoucherId.ShouldBeNull();
    }

    [Fact]
    public async Task Posting_a_purchase_moves_the_goods_the_debt_and_the_books()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        decimal stockBefore = await ClosingDebitAsync(client, "STOCK");
        decimal inputTaxBefore = await ClosingDebitAsync(client, "VAT-INPUT");

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 5m);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        PostPurchaseInvoiceResponse result =
            (await posted.Content.ReadFromJsonAsync<PostPurchaseInvoiceResponse>())!;

        result.Total.ShouldBe(105m);
        result.StockDocumentNumber.ShouldStartWith("PR");
        result.BillId.ShouldNotBeNull();

        // The goods are on the shelf at what they cost, and the tax is reclaimable.
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockBefore + 100m);
        (await ClosingDebitAsync(client, "VAT-INPUT")).ShouldBe(inputTaxBefore + 5m);
    }

    [Fact]
    public async Task The_clearing_account_nets_to_nothing_once_both_halves_have_landed()
    {
        // The claim the whole goods-received model rests on. The receipt credits it what
        // arrived and the invoice debits it back, in one transaction, so a firm that
        // receives and invoices together never sees a balance here at all.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        decimal debitBefore = await ClosingDebitAsync(client, "GOODS-RECEIVED");
        decimal creditBefore = await ClosingCreditAsync(client, "GOODS-RECEIVED");

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 5m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        decimal debited = await ClosingDebitAsync(client, "GOODS-RECEIVED") - debitBefore;
        decimal credited = await ClosingCreditAsync(client, "GOODS-RECEIVED") - creditBefore;

        (debited - credited).ShouldBe(0m);
    }

    [Fact]
    public async Task A_posted_purchase_names_what_its_posting_produced()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 2m, 50m, 5m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        PurchaseInvoiceDetail detail =
            (await client.GetFromJsonAsync<PurchaseInvoiceDetail>(
                $"{Purchases}/{purchaseId}"))!;

        detail.Header.Status.ShouldBe(PurchaseInvoiceStatus.Posted);
        detail.StockDocumentId.ShouldNotBeNull();
        detail.BillId.ShouldNotBeNull();
        detail.JournalVoucherId.ShouldNotBeNull();
    }

    [Fact]
    public async Task What_the_firm_owes_the_supplier_reaches_the_creditors()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 5m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The bill and the ledger read the same event, so both say 105.
        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(105m);
    }

    [Fact]
    public async Task A_purchase_of_a_batched_product_opens_the_batch_the_supplier_named()
    {
        // What makes a purchase different from a sale. The batch does not exist until
        // this posts; the receipt opens it from the number printed on the carton.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client, batched: true);

        string lot = $"LOT-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Purchases,
            new
            {
                Date = Today,
                SupplierLedgerId = fixtures.SupplierId,
                WarehouseId = fixtures.WarehouseId,
                SupplierInvoiceNumber = $"INV-{Guid.NewGuid().ToString("N")[..8]}",
                Lines = new[]
                {
                    new
                    {
                        ProductId = fixtures.ProductId,
                        Quantity = 6m,
                        Rate = 10m,
                        TaxPercentage = 0m,
                        BatchNumber = lot,
                    },
                },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        Guid purchaseId =
            (await created.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!
            .PurchaseInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/post", new { });

        posted.StatusCode.ShouldBe(
            HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        // The batch is on file, holding what arrived, at what it cost.
        BatchStockRow batch = (await client.GetFromJsonAsync<IReadOnlyList<BatchStockRow>>(
            $"{Inventory}/stock/batches?productId={fixtures.ProductId}"))!
            .Single(row => string.Equals(row.BatchNumber, lot, StringComparison.Ordinal));

        batch.Quantity.ShouldBe(6m);
        batch.PurchaseRate.ShouldBe(10m);
    }

    [Fact]
    public async Task Cancelling_a_posted_purchase_takes_the_goods_and_the_books_back()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        decimal stockBefore = await ClosingDebitAsync(client, "STOCK");
        decimal inputTaxBefore = await ClosingDebitAsync(client, "VAT-INPUT");

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 5m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/cancel",
            new { Reason = "Entered against the wrong supplier" });

        cancelled.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        // Both journals out of the balances, so the accounts stand where they did.
        (await ClosingDebitAsync(client, "STOCK")).ShouldBe(stockBefore);
        (await ClosingDebitAsync(client, "VAT-INPUT")).ShouldBe(inputTaxBefore);

        PurchaseInvoiceDetail detail =
            (await client.GetFromJsonAsync<PurchaseInvoiceDetail>(
                $"{Purchases}/{purchaseId}"))!;

        detail.Header.Status.ShouldBe(PurchaseInvoiceStatus.Cancelled);

        // And it still names what it produced, because the documents are all still there.
        detail.StockDocumentId.ShouldNotBeNull();
        detail.BillId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_cancelled_purchase_stops_showing_in_what_the_firm_owes()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 5m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(105m);

        (await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/cancel", new { Reason = "Entered twice" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The creditors figure and the credit position read the same bills, so
        // withdrawing one takes it out of both at once.
        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(0m);
    }

    [Fact]
    public async Task A_purchase_whose_goods_have_gone_cannot_be_cancelled()
    {
        // Where this differs from cancelling a sale, and the reason is physical: taking a
        // receipt back removes stock from a shelf, and a purchase whose goods have since
        // been sold has nothing left to remove. What the firm has is a return or a
        // write-off, not a cancellation.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 0m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Issued to a department, which is the simplest way for the goods to leave.
        HttpResponseMessage issued = await client.PostAsJsonAsync(
            $"{Inventory}/stock/documents",
            new
            {
                Type = (int)StockDocumentType.MaterialIssue,
                Date = Today,
                WarehouseId = fixtures.WarehouseId,
                Lines = new[] { new { ProductId = fixtures.ProductId, Quantity = 4m } },
            });

        issued.StatusCode.ShouldBe(
            HttpStatusCode.Created, await issued.Content.ReadAsStringAsync());

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/cancel", new { Reason = "Too late" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // And the purchase is still posted, so the books still say what happened.
        PurchaseInvoiceDetail detail =
            (await client.GetFromJsonAsync<PurchaseInvoiceDetail>(
                $"{Purchases}/{purchaseId}"))!;

        detail.Header.Status.ShouldBe(PurchaseInvoiceStatus.Posted);
    }

    [Fact]
    public async Task A_draft_cannot_be_cancelled_and_a_reason_is_required()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 1m, 10m);

        // A draft has moved nothing, so there is nothing to put back.
        (await client.PostAsJsonAsync(
            $"{Purchases}/{purchaseId}/cancel", new { Reason = "Changed my mind" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // A cancellation nobody explained is one somebody has to reconstruct later.
        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/cancel", new { Reason = " " }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelling_a_return_puts_back_what_it_set_against_the_purchase()
    {
        // The allocation is a fact about a bill, and cancelling the return's journal does
        // not remove it. Left behind, the purchase would read as settled by a debit note
        // that no longer exists and the creditors report would understate what is owed.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid purchaseId = await EnterAsync(client, fixtures, 4m, 25m, 0m);

        (await client.PostAsJsonAsync($"{Purchases}/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(100m);

        Guid returnId = await EnterReturnAsync(client, fixtures, purchaseId, 2m, 25m);

        (await client.PostAsJsonAsync($"{Purchases}/{returnId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Half the goods went back, so half the debt is settled.
        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(50m);

        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Purchases}/{returnId}/cancel", new { Reason = "The lorry came back" });

        cancelled.StatusCode.ShouldBe(
            HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        // The whole debt again, because the debit note is gone.
        (await OutstandingAsync(client, fixtures.SupplierId)).ShouldBe(100m);
    }

    [Fact]
    public async Task A_purchase_billed_by_somebody_who_is_not_a_supplier_is_refused()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        Guid customerId = await CustomerEndpointTests.CreateAsync(client);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Purchases,
            Purchase(fixtures with { SupplierId = customerId }, 1m, 10m, 0m));

        // A business rule rather than a malformed request: the document is well formed
        // and the party is simply the wrong kind of account.
        created.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_same_supplier_invoice_number_twice_is_refused()
    {
        // Input tax reclaimed twice is the expensive kind of duplicate: it is caught by
        // an assessor rather than by a reader of the creditors report.
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        PurchaseFixtures fixtures = await ArrangeAsync(client);

        string reference = $"INV-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        (await client.PostAsJsonAsync(
            Purchases, Purchase(fixtures, 1m, 10m, 0m, reference)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage again = await client.PostAsJsonAsync(
            Purchases, Purchase(fixtures, 1m, 10m, 0m, reference));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------ scaffolding

    private static object Purchase(
        PurchaseFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage,
        string? supplierInvoiceNumber = null) =>
        new
        {
            Date = Today,
            SupplierLedgerId = fixtures.SupplierId,
            WarehouseId = fixtures.WarehouseId,
            SupplierInvoiceNumber = supplierInvoiceNumber
                ?? $"INV-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            SupplierInvoiceDate = Today,
            Lines = new[]
            {
                new
                {
                    ProductId = fixtures.ProductId,
                    Quantity = quantity,
                    Rate = rate,
                    TaxPercentage = taxPercentage,
                },
            },
        };

    private static async Task<Guid> EnterAsync(
        HttpClient client,
        PurchaseFixtures fixtures,
        decimal quantity,
        decimal rate,
        decimal taxPercentage = 0m)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Purchases, Purchase(fixtures, quantity, rate, taxPercentage));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!
            .PurchaseInvoiceId;
    }

    /// <summary>Enters a return against a posted purchase.</summary>
    private static async Task<Guid> EnterReturnAsync(
        HttpClient client,
        PurchaseFixtures fixtures,
        Guid against,
        decimal quantity,
        decimal rate)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Purchases,
            new
            {
                Date = Today,
                SupplierLedgerId = fixtures.SupplierId,
                WarehouseId = fixtures.WarehouseId,
                Kind = (int)PurchaseDocumentKind.Return,
                ReturnsInvoiceId = against,
                Lines = new[]
                {
                    new
                    {
                        ProductId = fixtures.ProductId,
                        Quantity = quantity,
                        Rate = rate,
                        TaxPercentage = 0m,
                    },
                },
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!
            .PurchaseInvoiceId;
    }

    /// <summary>What the firm owes, read the way the credit screen reads it.</summary>
    private static async Task<decimal> OutstandingAsync(HttpClient client, Guid supplierId)
    {
        CreditStatus status = (await client.GetFromJsonAsync<CreditStatus>(
            $"/api/v1/accounting/ledgers/{supplierId}/credit-status"))!;

        return status.Outstanding;
    }

    private static async Task<decimal> ClosingDebitAsync(HttpClient client, string code) =>
        (await RowAsync(client, code))?.ClosingDebit ?? 0m;

    private static async Task<decimal> ClosingCreditAsync(HttpClient client, string code) =>
        (await RowAsync(client, code))?.ClosingCredit ?? 0m;

    private static async Task<TrialBalanceRow?> RowAsync(HttpClient client, string code)
    {
        DateOnly from = new(Today.Year, 1, 1);

        TrialBalanceResponse report = (await client.GetFromJsonAsync<TrialBalanceResponse>(
            $"{Reports}/trial-balance?from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))!;

        return report.Rows.FirstOrDefault(row =>
            string.Equals(row.LedgerCode, code, StringComparison.Ordinal));
    }

    /// <summary>Creates the masters a purchase needs: a supplier, a warehouse, a product.</summary>
    private static async Task<PurchaseFixtures> ArrangeAsync(
        HttpClient client,
        bool batched = false)
    {
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        Guid categoryId = await CreateMasterAsync(
            client, "categories", new { Code = $"PCAT{suffix}", Name = $"Purchase {suffix}" });

        Guid unitId = await CreateMasterAsync(
            client, "units", new { Code = $"PEA{suffix}", Name = "Each" });

        Guid warehouseId = await CreateMasterAsync(
            client, "warehouses", new { Code = $"PWH{suffix}", Name = "Purchase store" });

        HttpResponseMessage product = await client.PostAsJsonAsync(
            $"{Inventory}/products",
            new
            {
                Code = $"PUR{suffix}",
                Description = "A thing to buy",
                CategoryId = categoryId,
                StockUnitId = unitId,
                ItemType = 1,
            });

        product.StatusCode.ShouldBe(
            HttpStatusCode.Created, await product.Content.ReadAsStringAsync());

        Guid productId = await product.Content.ReadFromJsonAsync<Guid>();

        if (batched)
        {
            HttpResponseMessage tracked = await client.PutAsJsonAsync(
                $"{Inventory}/products/{productId}/stocking",
                new { PurchaseUnitId = unitId, SalesUnitId = unitId, TracksBatches = true });

            tracked.StatusCode.ShouldBe(
                HttpStatusCode.NoContent, await tracked.Content.ReadAsStringAsync());
        }

        HttpResponseMessage supplier = await client.PostAsJsonAsync(
            Suppliers,
            new { Code = $"SUP{suffix}", Name = $"Gulf Wholesale {suffix}" });

        supplier.StatusCode.ShouldBe(
            HttpStatusCode.Created, await supplier.Content.ReadAsStringAsync());

        Guid supplierId =
            (await supplier.Content.ReadFromJsonAsync<SupplierResponse>())!.SupplierId;

        return new PurchaseFixtures(supplierId, warehouseId, productId);
    }

    private static async Task<Guid> CreateMasterAsync(
        HttpClient client,
        string resource,
        object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Inventory}/{resource}", body);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private sealed record PurchaseFixtures(Guid SupplierId, Guid WarehouseId, Guid ProductId);
}
