using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Sales;

/// <summary>Tests for cancelling a posted invoice.</summary>
/// <remarks>
/// The mirror of posting, and it is checked against posting rather than on its own: the
/// same fixture posts a sale and then takes it back, so what these prove is that the two
/// halves agree. A cancellation that put back a different quantity, or at a different
/// cost, would leave a stock ledger nobody could reconcile against a shelf.
/// </remarks>
public sealed class CancelSalesInvoiceTests
{
    [Fact]
    public async Task Cancelling_puts_the_goods_back_and_takes_the_debt_away()
    {
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);

        (await fixture.Create(quantity: 2m, rate: 100m, taxPercentage: 5m))
            .IsSuccess.ShouldBeTrue();
        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        fixture.Position!.Quantity.ShouldBe(98m);

        Result cancelled = await fixture.Cancel();

        cancelled.IsSuccess.ShouldBeTrue(
            cancelled.IsFailure ? cancelled.Error.Description : null);

        fixture.Created.Status.ShouldBe(SalesInvoiceStatus.Cancelled);
        fixture.Position.Quantity.ShouldBe(100m);
        fixture.Raised.ShouldHaveSingleItem().Status.ShouldBe(BillStatus.Cancelled);
    }

    [Fact]
    public async Task Both_journals_leave_the_balances_and_neither_leaves_the_books()
    {
        // Cancelled rather than reversed by contras: a voucher's own cancellation keeps
        // its number and its lines, which is how every other cancelled voucher here
        // behaves, and a day book with two entries for one mistake helps nobody.
        SalesPostingFixture fixture = new();

        await fixture.Create();
        await fixture.Post();

        (await fixture.Cancel()).IsSuccess.ShouldBeTrue();

        fixture.Journals.Count.ShouldBe(2);
        fixture.Journals.ShouldAllBe(journal => journal.Status == VoucherStatus.Cancelled);
    }

    [Fact]
    public async Task The_issue_is_cancelled_along_with_the_invoice()
    {
        SalesPostingFixture fixture = new();

        await fixture.Create();
        await fixture.Post();

        (await fixture.Cancel()).IsSuccess.ShouldBeTrue();

        fixture.Issued.ShouldHaveSingleItem().Status.ShouldBe(StockDocumentStatus.Cancelled);
    }

    [Fact]
    public async Task The_goods_go_back_at_what_they_left_at()
    {
        // Not at today's average. Reversing at a rate the goods never moved at would
        // restate the value of everything else on the shelf along with them.
        SalesPostingFixture fixture = new(onHand: 10m, unitCost: 60m);

        await fixture.Create(quantity: 4m, rate: 100m, taxPercentage: 5m);
        await fixture.Post();

        fixture.Position!.AverageCost.ShouldBe(60m);

        (await fixture.Cancel()).IsSuccess.ShouldBeTrue();

        fixture.Position.Quantity.ShouldBe(10m);
        fixture.Position.AverageCost.ShouldBe(60m);
    }

    [Fact]
    public async Task An_invoice_the_customer_has_paid_against_is_refused()
    {
        // The refusal that sends somebody to the right document. A receipt has to stay
        // where it was made, so goods that come back after they were paid for are a
        // credit note rather than a cancellation.
        SalesPostingFixture fixture = new();

        await fixture.Create(quantity: 2m, rate: 100m, taxPercentage: 5m);
        await fixture.Post();

        Bill bill = fixture.Raised.ShouldHaveSingleItem();

        bill.Allocate(VoucherId.NewId(), Money.Of(50m, CurrencyCode.Qar), bill.BillDate)
            .IsSuccess.ShouldBeTrue();

        Result cancelled = await fixture.Cancel();

        cancelled.Error.Code.ShouldBe("Bill.PartlySettled");

        // And nothing else moved on the way to finding that out.
        fixture.Created.Status.ShouldBe(SalesInvoiceStatus.Posted);
        fixture.Position!.Quantity.ShouldBe(98m);
    }

    [Fact]
    public async Task A_draft_has_nothing_to_cancel()
    {
        SalesPostingFixture fixture = new();

        await fixture.Create();

        (await fixture.Cancel()).Error.Code.ShouldBe("SalesInvoice.NotPosted");
    }

    [Fact]
    public async Task An_invoice_cannot_be_cancelled_twice()
    {
        SalesPostingFixture fixture = new();

        await fixture.Create();
        await fixture.Post();

        (await fixture.Cancel()).IsSuccess.ShouldBeTrue();
        (await fixture.Cancel()).Error.Code.ShouldBe("SalesInvoice.NotPosted");
    }

    [Fact]
    public async Task An_invoice_of_another_firm_is_not_found()
    {
        SalesPostingFixture fixture = new();

        await fixture.Create();
        await fixture.Post();
        fixture.Holds(null);

        (await fixture.Cancel()).Error.Code.ShouldBe("SalesInvoice.NotFound");
    }
}
