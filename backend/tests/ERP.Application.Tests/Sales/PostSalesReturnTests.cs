using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Sales;

/// <summary>Tests for posting a sales return.</summary>
/// <remarks>
/// Checked against a sale rather than on its own: each of these sells something first and
/// then takes it back, because what matters is that the two agree. A return that put back
/// a different quantity, or credited a different figure, would leave a customer's account
/// and a stock ledger that no reconciliation could bring together.
/// </remarks>
public sealed class PostSalesReturnTests
{
    [Fact]
    public async Task A_return_puts_the_goods_back_and_credits_the_customer()
    {
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        fixture.Position!.Quantity.ShouldBe(98m);

        Result<PostSalesInvoiceResponse> returned = await ReturnAsync(
            fixture, quantity: 2m, against: soldOn);

        returned.IsSuccess.ShouldBeTrue(
            returned.IsFailure ? returned.Error.Description : null);

        fixture.Position.Quantity.ShouldBe(100m);
        fixture.Net(fixture.Customer.Id).ShouldBe(0m);
    }

    [Fact]
    public async Task Revenue_and_the_tax_come_back_off_their_own_accounts()
    {
        SalesPostingFixture fixture = new();

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        await ReturnAsync(fixture, quantity: 2m, against: soldOn);

        // Gross sales still says what went out; the return says what came back. Netting
        // them into one account would make the first figure unanswerable.
        fixture.Net(fixture.AccountLedgers[StockAccount.SalesRevenue].Id).ShouldBe(-200m);
        fixture.Net(fixture.AccountLedgers[StockAccount.SalesReturn].Id).ShouldBe(200m);
        fixture.Net(fixture.OutputVat.Id).ShouldBe(0m);
    }

    [Fact]
    public async Task The_cost_of_the_sale_is_credited_back_by_the_receipt()
    {
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        await ReturnAsync(fixture, quantity: 2m, against: soldOn);

        // Two at sixty out, two at sixty back: the cost of goods sold nets to nothing,
        // which is the whole point of returning at the cost the goods left at.
        fixture.Net(fixture.AccountLedgers[StockAccount.CostOfGoodsSold].Id).ShouldBe(0m);
        fixture.Net(fixture.AccountLedgers[StockAccount.Inventory].Id).ShouldBe(0m);
    }

    [Fact]
    public async Task Goods_come_back_at_the_cost_they_left_at()
    {
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        await ReturnAsync(fixture, quantity: 2m, against: soldOn);

        StockLedgerEntry receipt = fixture.Movements[^1];

        receipt.DocumentType.ShouldBe(StockDocumentType.SalesReturn);
        receipt.UnitCost.ShouldBe(60m);
    }

    [Fact]
    public async Task A_return_naming_no_invoice_comes_back_at_the_average()
    {
        // There is no original cost to read, and receiving at the average cannot move
        // the average - so the rest of the shelf is left where it was.
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);

        (await fixture.Create(quantity: 2m, rate: 100m, taxPercentage: 5m,
            kind: SalesDocumentKind.Return)).IsSuccess.ShouldBeTrue();

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        fixture.Movements.ShouldHaveSingleItem().UnitCost.ShouldBe(60m);
        fixture.Position!.AverageCost.ShouldBe(60m);
        fixture.Position.Quantity.ShouldBe(102m);
    }

    [Fact]
    public async Task A_return_settles_the_bill_the_sale_raised()
    {
        SalesPostingFixture fixture = new();

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        Bill bill = fixture.Raised.ShouldHaveSingleItem();

        bill.Status.ShouldBe(BillStatus.Open);

        await ReturnAsync(fixture, quantity: 2m, against: soldOn);

        // Settled by the goods coming back rather than left showing as still owing.
        bill.Status.ShouldBe(BillStatus.Settled);
        fixture.Raised.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_raises_no_bill_of_its_own()
    {
        SalesPostingFixture fixture = new();

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        Result<PostSalesInvoiceResponse> returned = await ReturnAsync(
            fixture, quantity: 2m, against: soldOn);

        // It credits a debt rather than creating one.
        returned.Value.BillId.ShouldBeNull();
    }

    [Fact]
    public async Task A_return_worth_more_than_the_bill_still_owes_is_capped_not_refused()
    {
        // Ordinary once a customer has part-paid. The excess is a credit on their
        // account; refusing would leave somebody at a counter with goods nobody will
        // take back.
        SalesPostingFixture fixture = new();

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        Bill bill = fixture.Raised.ShouldHaveSingleItem();

        bill.Allocate(VoucherId.NewId(), Money.Of(100m, CurrencyCode.Qar), bill.BillDate)
            .IsSuccess.ShouldBeTrue();

        Result<PostSalesInvoiceResponse> returned = await ReturnAsync(
            fixture, quantity: 2m, against: soldOn);

        returned.IsSuccess.ShouldBeTrue(
            returned.IsFailure ? returned.Error.Description : null);

        bill.Status.ShouldBe(BillStatus.Settled);
        bill.OutstandingAmount.Amount.ShouldBe(0m);
    }

    [Fact]
    public async Task A_return_is_numbered_from_a_series_of_its_own()
    {
        // A credit note is not a gap in the invoice sequence.
        SalesPostingFixture fixture = new();

        Guid soldOn = await SellAsync(fixture, quantity: 2m);

        Result<SalesInvoiceResponse> credit = await fixture.Create(
            quantity: 1m, rate: 100m, taxPercentage: 5m,
            kind: SalesDocumentKind.Return, returnsInvoiceId: soldOn);

        credit.Value.Number.ShouldStartWith("SR");

        Result<PostSalesInvoiceResponse> posted = await fixture.Post();

        posted.Value.StockDocumentNumber.ShouldStartWith("SR");
    }

    [Fact]
    public async Task A_return_against_an_invoice_of_another_firm_is_refused()
    {
        SalesPostingFixture fixture = new();

        (await fixture.Create(quantity: 1m, rate: 100m, taxPercentage: 5m,
            kind: SalesDocumentKind.Return, returnsInvoiceId: Guid.NewGuid()))
            .IsSuccess.ShouldBeTrue();

        (await fixture.Post()).Error.Code.ShouldBe("SalesReturn.InvoiceNotFound");
    }

    /// <summary>Sells something, and answers with the invoice it sold it on.</summary>
    private static async Task<Guid> SellAsync(SalesPostingFixture fixture, decimal quantity)
    {
        (await fixture.Create(quantity, rate: 100m, taxPercentage: 5m))
            .IsSuccess.ShouldBeTrue();

        Guid invoiceId = fixture.Created.Id.Value;

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        return invoiceId;
    }

    /// <summary>Takes it back again.</summary>
    private static async Task<Result<PostSalesInvoiceResponse>> ReturnAsync(
        SalesPostingFixture fixture,
        decimal quantity,
        Guid against)
    {
        (await fixture.Create(
            quantity, rate: 100m, taxPercentage: 5m,
            kind: SalesDocumentKind.Return, returnsInvoiceId: against)).IsSuccess.ShouldBeTrue();

        return await fixture.Post();
    }
}
