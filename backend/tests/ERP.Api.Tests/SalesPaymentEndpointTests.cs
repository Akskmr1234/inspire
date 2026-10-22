using System.Net;
using System.Net.Http.Json;
using ERP.Application.Abstractions;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;

namespace ERP.Api.Tests;

/// <summary>
/// What a customer pays with a sale: in cash or online, in full or in part, received into
/// the account it went to and set against the sale's own bill - and the customer's account
/// as the counter sees it beside the next sale.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SalesPaymentEndpointTests
{
    private const string Invoices = "/api/v1/sales/invoices";
    private const string Customers = "/api/v1/sales/customers";
    private const string Vouchers = "/api/v1/accounting/vouchers";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="SalesPaymentEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public SalesPaymentEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_cash_sale_paid_in_full_is_settled_when_it_posts_and_its_receipt_goes_if_it_is_cancelled()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);
        Guid cash = await LedgerOfKindAsync(client, LedgerKind.Cash);

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            AmountReceived = 15m,
            ReceivedIntoLedgerId = cash,
            Post = true,
        }));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"{Invoices}/{invoice.SalesInvoiceId}"))!;
        detail.AmountReceived.ShouldBe(15m);
        detail.ReceivedIntoLedgerId.ShouldBe(cash);
        detail.ReceiptVoucherId.ShouldNotBeNull();

        // An ordinary cash receipt, in the cash book where any other would be.
        VoucherDetailResponse receipt = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"{Vouchers}/{detail.ReceiptVoucherId}"))!;
        receipt.Type.ShouldBe(VoucherType.CashReceipt);
        receipt.Status.ShouldBe(VoucherStatus.Posted);
        receipt.TotalDebit.ShouldBe(15m);
        receipt.PaymentMode.ShouldBe("Cash");
        receipt.Lines.ShouldContain(line => line.LedgerId == cash && line.Side == EntrySide.Debit);
        receipt.Lines.ShouldContain(line =>
            line.LedgerId == masters.CustomerId && line.Side == EntrySide.Credit);

        // Nothing is left owing, either way it is counted.
        CustomerAccount account = await AccountAsync(client, masters.CustomerId);
        account.Balance.ShouldBe(0m);
        account.Outstanding.ShouldBe(0m);

        // Cancelling the sale takes its receipt with it rather than being refused for it.
        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Invoices}/{invoice.SalesInvoiceId}/cancel", new { Reason = "Rung up twice" });
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        (await client.GetFromJsonAsync<VoucherDetailResponse>($"{Vouchers}/{detail.ReceiptVoucherId}"))!
            .Status.ShouldBe(VoucherStatus.Cancelled);

        account = await AccountAsync(client, masters.CustomerId);
        account.Balance.ShouldBe(0m);
        account.Outstanding.ShouldBe(0m);
    }

    [Fact]
    public async Task An_online_part_payment_goes_to_the_bank_with_its_transaction_id_and_follows_an_edit()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);
        Guid bank = await LedgerOfKindAsync(client, LedgerKind.Bank);

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Online,
            AmountReceived = 10m,
            ReceivedIntoLedgerId = bank,
            PaymentReference = "  GPAY-4471-2290  ",
            Post = true,
        }));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid id = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{id}"))!;
        detail.PaymentMode.ShouldBe(SalesPaymentMode.Online);
        detail.PaymentReference.ShouldBe("GPAY-4471-2290");

        VoucherDetailResponse receipt = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"{Vouchers}/{detail.ReceiptVoucherId}"))!;
        receipt.Type.ShouldBe(VoucherType.BankReceipt);
        receipt.ReferenceNumber.ShouldBe("GPAY-4471-2290");
        receipt.PaymentMode.ShouldBe("Online");

        // Paid ten of fifteen: five is still owed, on the bill and on the account.
        CustomerAccount account = await AccountAsync(client, masters.CustomerId);
        account.Balance.ShouldBe(5m);
        account.Outstanding.ShouldBe(5m);

        PagedResult<SalesInvoiceSummary> page = (await client.GetFromJsonAsync<PagedResult<SalesInvoiceSummary>>(
            $"{Invoices}?customerLedgerId={masters.CustomerId}"))!;
        SalesInvoiceSummary row = page.Items.Single(item => item.SalesInvoiceId == id);
        row.AmountReceived.ShouldBe(10m);
        row.PaymentReference.ShouldBe("GPAY-4471-2290");

        // Corrected to two units, paid in full: the first receipt is cancelled and the
        // corrected sale raises its own, and the invoice keeps its number.
        HttpResponseMessage edited = await client.PutAsJsonAsync($"{Invoices}/{id}", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Lines = new[] { new { masters.ProductId, Quantity = 2m, Rate = 15m, TaxPercentage = 0m } },
            PaymentMode = SalesPaymentMode.Online,
            AmountReceived = 30m,
            ReceivedIntoLedgerId = bank,
            PaymentReference = "GPAY-4471-2291",
            Reason = "Two were taken",
        });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

        SalesInvoiceDetail corrected = (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{id}"))!;
        corrected.Header.Status.ShouldBe(SalesInvoiceStatus.Posted);
        corrected.ReceiptVoucherId.ShouldNotBeNull();
        corrected.ReceiptVoucherId.ShouldNotBe(detail.ReceiptVoucherId);

        (await client.GetFromJsonAsync<VoucherDetailResponse>($"{Vouchers}/{detail.ReceiptVoucherId}"))!
            .Status.ShouldBe(VoucherStatus.Cancelled);

        account = await AccountAsync(client, masters.CustomerId);
        account.Balance.ShouldBe(0m);
        account.Outstanding.ShouldBe(0m);
    }

    [Fact]
    public async Task A_card_payment_goes_to_the_bank_with_its_approval_code()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);
        Guid bank = await LedgerOfKindAsync(client, LedgerKind.Bank);

        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Card,
            AmountReceived = 15m,
            ReceivedIntoLedgerId = bank,
            PaymentReference = "APPR-0091",
            Post = true,
        }));
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid id = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{id}"))!;
        detail.PaymentMode.ShouldBe(SalesPaymentMode.Card);

        VoucherDetailResponse receipt = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"{Vouchers}/{detail.ReceiptVoucherId}"))!;
        receipt.Type.ShouldBe(VoucherType.BankReceipt);
        receipt.ReferenceNumber.ShouldBe("APPR-0091");
        receipt.PaymentMode.ShouldBe("Card");
    }

    [Fact]
    public async Task A_payment_must_fit_the_bill_and_go_into_a_cash_or_bank_account()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);
        Guid cash = await LedgerOfKindAsync(client, LedgerKind.Cash);

        int before = await CountAsync(client, masters.CustomerId);

        // More than the bill: change handed back is not money received. Refused, and the
        // sale saved in the same request is not left behind as a draft.
        HttpResponseMessage tooMuch = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            AmountReceived = 20m,
            ReceivedIntoLedgerId = cash,
            Post = true,
        }));
        tooMuch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await tooMuch.Content.ReadAsStringAsync());
        (await CountAsync(client, masters.CustomerId)).ShouldBe(before);

        // Somewhere the money went is required.
        HttpResponseMessage nowhere = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            AmountReceived = 15m,
        }));
        nowhere.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await nowhere.Content.ReadAsStringAsync());

        // And it is a cash or bank account, not the customer's own.
        HttpResponseMessage intoCustomer = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Cash,
            AmountReceived = 15m,
            ReceivedIntoLedgerId = masters.CustomerId,
        }));
        intoCustomer.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await intoCustomer.Content.ReadAsStringAsync());

        // A sale on account takes nothing and raises no receipt.
        HttpResponseMessage credit = await client.PostAsJsonAsync(Invoices, Sale(masters, new
        {
            PaymentMode = SalesPaymentMode.Credit,
            Post = true,
        }));
        credit.StatusCode.ShouldBe(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());
        Guid id = (await credit.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>($"{Invoices}/{id}"))!;
        detail.ReceiptVoucherId.ShouldBeNull();

        CustomerAccount account = await AccountAsync(client, masters.CustomerId);
        account.Outstanding.ShouldBe(15m);
        account.Balance.ShouldBe(15m);

        // Somebody who is not a customer has no customer account.
        (await client.GetAsync($"{Customers}/{masters.SupplierId}/account"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Paid_in_full_takes_the_rounded_total_the_server_bills()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        await BuyAsync(client, masters);
        Guid cash = await LedgerOfKindAsync(client, LedgerKind.Cash);

        // 10.333 plus 5% tax is 10.84965, billed as 10.85. A counter that worked the
        // figure out for itself could be a fraction either side; in full means the bill.
        HttpResponseMessage sold = await client.PostAsJsonAsync(Invoices, new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            WarehouseId = masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 10.333m, TaxPercentage = 5m } },
            PaymentMode = SalesPaymentMode.Cash,
            PaidInFull = true,
            ReceivedIntoLedgerId = cash,
            Post = true,
        });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"{Invoices}/{invoice.SalesInvoiceId}"))!;
        detail.AmountReceived.ShouldBe(invoice.Total);

        (await AccountAsync(client, masters.CustomerId)).Outstanding.ShouldBe(0m);
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

    private static async Task<Guid> LedgerOfKindAsync(HttpClient client, LedgerKind kind) =>
        (await client.GetFromJsonAsync<List<LedgerSummary>>("/api/v1/accounting/ledgers"))!
            .First(ledger => ledger.Kind == kind)
            .LedgerId;

    private static async Task<CustomerAccount> AccountAsync(HttpClient client, Guid customerId) =>
        (await client.GetFromJsonAsync<CustomerAccount>($"{Customers}/{customerId}/account"))!;

    private static async Task<int> CountAsync(HttpClient client, Guid customerId) =>
        (await client.GetFromJsonAsync<PagedResult<SalesInvoiceSummary>>(
            $"{Invoices}?customerLedgerId={customerId}"))!.TotalCount;

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
