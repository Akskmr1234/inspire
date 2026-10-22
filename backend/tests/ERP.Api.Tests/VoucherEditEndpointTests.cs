using System.Net;
using System.Net.Http.Json;
using ERP.Application.Accounting.Reports;
using ERP.Application.Accounting.Vouchers;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;

namespace ERP.Api.Tests;

/// <summary>A receipt, payment or journal can be corrected under its number, or cancelled.</summary>
/// <remarks>
/// The voucher side of the firm's request for an edit and a cancel on every transaction:
/// what a receipt settled is owed again once it is cancelled, a correction moves the
/// settlement with it, and the journal an invoice wrote stays the invoice's to change.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class VoucherEditEndpointTests
{
    private const string Vouchers = "/api/v1/accounting/vouchers";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    public VoucherEditEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_receipt_is_corrected_under_its_number_and_cancelling_it_puts_the_debt_back()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client, billWise: true);
        Guid cash = await CashLedgerAsync(client);
        SalesInvoiceDetail sale = await SellAsync(client, masters, 1000m);

        (await OutstandingAsync(client, masters.CustomerId)).ShouldBe(1000m);

        // Received 400 against the sale.
        HttpResponseMessage received = await client.PostAsJsonAsync(Vouchers, Receipt(cash, masters, sale, 400m));
        received.StatusCode.ShouldBe(HttpStatusCode.Created, await received.Content.ReadAsStringAsync());
        CreateVoucherResponse receipt = (await received.Content.ReadFromJsonAsync<CreateVoucherResponse>())!;

        (await OutstandingAsync(client, masters.CustomerId)).ShouldBe(600m);

        // The next receipt for the customer offers the sale with what is left of it.
        OutstandingBill open = (await client.GetFromJsonAsync<List<OutstandingBill>>(
            $"{Vouchers}/open-bills?ledgerId={masters.CustomerId}&type={(int)BillType.Receivable}"))!.Single();
        open.BillNumber.ShouldBe(sale.Header.Number);
        open.OutstandingAmount.ShouldBe(600m);
        (await client.GetFromJsonAsync<List<OutstandingBill>>(
            $"{Vouchers}/open-bills?ledgerId={masters.CustomerId}&type={(int)BillType.Payable}"))!.ShouldBeEmpty();

        // It was 450.
        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"{Vouchers}/{receipt.VoucherId}",
            new
            {
                Date = Today,
                Lines = Receipt(cash, masters, sale, 450m).Lines,
                Narration = "Counted again",
                Reason = "Miscounted",
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        CreateVoucherResponse corrected = (await edited.Content.ReadFromJsonAsync<CreateVoucherResponse>())!;

        corrected.Number.ShouldBe(receipt.Number);
        corrected.Status.ShouldBe(VoucherStatus.Posted);
        corrected.TotalDebit.ShouldBe(450m);
        (await OutstandingAsync(client, masters.CustomerId)).ShouldBe(550m);

        // Reopened to correct again, it says what it settled, so the receipt screen can
        // show the sale ticked.
        VoucherDetailResponse reopened = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"{Vouchers}/{receipt.VoucherId}"))!;
        VoucherSettlementResponse settled = reopened.Settlements.ShouldNotBeNull().ShouldHaveSingleItem();
        settled.BillNumber.ShouldBe(sale.Header.Number);
        settled.Amount.ShouldBe(450m);
        settled.Outstanding.ShouldBe(550m);

        // And the receipts list finds it among cash and bank receipts, naming both sides.
        VoucherReportResponse receipts = (await client.GetFromJsonAsync<VoucherReportResponse>(
            $"{Vouchers}?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"
            + $"&types={(int)VoucherType.CashReceipt}&types={(int)VoucherType.BankReceipt}"))!;
        VoucherReportLine listed = receipts.Vouchers.Single(row => row.VoucherId == receipt.VoucherId);
        listed.DocumentAmount.ShouldBe(450m);
        listed.DebitLedgers.ShouldNotBeNullOrWhiteSpace();
        listed.CreditLedgers.ShouldNotBeNullOrWhiteSpace();
        receipts.Vouchers.ShouldAllBe(row =>
            row.Type == VoucherType.CashReceipt || row.Type == VoucherType.BankReceipt);

        // Cancelled, the customer owes the whole sale again.
        HttpResponseMessage cancelled = await client.PostAsJsonAsync(
            $"{Vouchers}/{receipt.VoucherId}/cancel", new { Reason = "Cheque never arrived" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());
        (await OutstandingAsync(client, masters.CustomerId)).ShouldBe(1000m);

        VoucherDetailResponse after = (await client.GetFromJsonAsync<VoucherDetailResponse>(
            $"{Vouchers}/{receipt.VoucherId}"))!;
        after.Status.ShouldBe(VoucherStatus.Cancelled);

        // And with the receipt gone, the sale can be corrected again.
        HttpResponseMessage resold = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{sale.Header.SalesInvoiceId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 900m, TaxPercentage = 0m } },
            });

        resold.StatusCode.ShouldBe(HttpStatusCode.OK, await resold.Content.ReadAsStringAsync());
        (await OutstandingAsync(client, masters.CustomerId)).ShouldBe(900m);
    }

    [Fact]
    public async Task A_paid_sale_is_not_edited_and_the_journal_it_wrote_is_not_cancelled_by_hand()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client, billWise: true);
        Guid cash = await CashLedgerAsync(client);
        SalesInvoiceDetail sale = await SellAsync(client, masters, 500m);

        HttpResponseMessage received = await client.PostAsJsonAsync(Vouchers, Receipt(cash, masters, sale, 500m));
        received.StatusCode.ShouldBe(HttpStatusCode.Created, await received.Content.ReadAsStringAsync());

        HttpResponseMessage edited = await client.PutAsJsonAsync(
            $"/api/v1/sales/invoices/{sale.Header.SalesInvoiceId}",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 450m, TaxPercentage = 0m } },
            });

        edited.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await edited.Content.ReadAsStringAsync()).ShouldContain("receipt");

        HttpResponseMessage journal = await client.PostAsJsonAsync(
            $"{Vouchers}/{sale.JournalVoucherId}/cancel", new { Reason = "Trying" });

        journal.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await journal.Content.ReadAsStringAsync()).ShouldContain(sale.Header.Number);
    }

    private static (object Body, object[] Lines) ReceiptParts(Guid cash, TestMasters masters, Guid billId, decimal amount)
    {
        object[] lines =
        [
            new { LedgerId = cash, Side = EntrySide.Debit, Amount = amount },
            new
            {
                LedgerId = masters.CustomerId,
                Side = EntrySide.Credit,
                Amount = amount,
                BillReferences = new[] { new { Kind = 2, Amount = amount, BillId = billId } },
            },
        ];

        return (new { Type = VoucherType.CashReceipt, Date = Today, Lines = lines }, lines);
    }

    private static ReceiptRequest Receipt(Guid cash, TestMasters masters, SalesInvoiceDetail sale, decimal amount)
    {
        (object _, object[] lines) = ReceiptParts(
            cash,
            masters,
            sale.BillId ?? throw new InvalidOperationException("The sale raised no bill."),
            amount);

        return new ReceiptRequest(VoucherType.CashReceipt, Today, lines);
    }

    private static async Task<SalesInvoiceDetail> SellAsync(HttpClient client, TestMasters masters, decimal rate)
    {
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        HttpResponseMessage bought = await client.PostAsJsonAsync(
            "/api/v1/purchase/invoices",
            new
            {
                Date = Today,
                SupplierLedgerId = masters.SupplierId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                SupplierInvoiceNumber = $"V-{tag}",
                Lines = new[] { new { masters.ProductId, Quantity = 5m, Rate = 10m, TaxPercentage = 0m } },
            });
        bought.StatusCode.ShouldBe(HttpStatusCode.Created, await bought.Content.ReadAsStringAsync());
        Guid purchaseId = (await bought.Content.ReadFromJsonAsync<PurchaseInvoiceResponse>())!.PurchaseInvoiceId;
        (await client.PostAsJsonAsync($"/api/v1/purchase/invoices/{purchaseId}/post", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage sold = await client.PostAsJsonAsync(
            "/api/v1/sales/invoices",
            new
            {
                Date = Today,
                CustomerLedgerId = masters.CustomerId,
                WarehouseId = masters.WarehouseId,
                Kind = 1,
                Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = rate, TaxPercentage = 0m } },
            });
        sold.StatusCode.ShouldBe(HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        Guid saleId = (await sold.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!.SalesInvoiceId;

        HttpResponseMessage posted = await client.PostAsJsonAsync($"/api/v1/sales/invoices/{saleId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        return (await client.GetFromJsonAsync<SalesInvoiceDetail>($"/api/v1/sales/invoices/{saleId}"))!;
    }

    private static async Task<Guid> CashLedgerAsync(HttpClient client)
    {
        List<LedgerRow> ledgers = (await client.GetFromJsonAsync<List<LedgerRow>>("/api/v1/accounting/ledgers"))!;

        return ledgers.First(ledger => ledger.Kind == LedgerKind.Cash).LedgerId;
    }

    private static async Task<decimal> OutstandingAsync(HttpClient client, Guid customerId) =>
        (await client.GetFromJsonAsync<CreditStatus>(
            $"/api/v1/accounting/ledgers/{customerId}/credit-status"))!.Outstanding;

    private sealed record ReceiptRequest(VoucherType Type, DateOnly Date, object[] Lines);

    private sealed record LedgerRow(Guid LedgerId, string Name, LedgerKind Kind);
}
