using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Tests.Sales;

/// <summary>Tests for <see cref="PostSalesInvoiceCommandHandler"/>.</summary>
/// <remarks>
/// The only place in the system where four aggregates move together. What these check is
/// not that each one works - each has its own tests - but that they move as one: that a
/// sale which cannot issue its stock does not raise a bill, and that a sale which can
/// leaves the goods, the debt and the books all describing the same event.
/// </remarks>
public sealed class PostSalesInvoiceTests
{
    [Fact]
    public async Task A_sale_issues_the_goods_raises_the_debt_and_states_both_in_the_books()
    {
        SalesPostingFixture fixture = new();
        SalesInvoice invoice = fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        Result<PostSalesInvoiceResponse> result = await fixture.Post();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);

        invoice.Status.ShouldBe(SalesInvoiceStatus.Posted);
        result.Value.Total.ShouldBe(210m);

        // All four, and the invoice names each of them afterwards.
        invoice.StockDocumentId.ShouldNotBeNull();
        invoice.BillId.ShouldNotBeNull();
        invoice.JournalVoucherId.ShouldNotBeNull();

        fixture.Issued.ShouldHaveSingleItem().Type.ShouldBe(StockDocumentType.SalesIssue);
        fixture.Raised.ShouldHaveSingleItem().OriginalAmount.Amount.ShouldBe(210m);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_goods_leave_the_shelf_at_what_they_cost()
    {
        // Two of a hundred sold from a position of a hundred bought at sixty.
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);
        fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        fixture.Position!.Quantity.ShouldBe(98m);
        fixture.Movements.ShouldHaveSingleItem().Value.Amount.ShouldBe(-120m);
    }

    [Fact]
    public async Task Revenue_tax_and_the_customer_are_stated_separately()
    {
        SalesPostingFixture fixture = new();
        SalesInvoice invoice = fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        fixture.Net(invoice.CustomerLedgerId).ShouldBe(210m);
        fixture.Net(fixture.AccountLedgers[StockAccount.SalesRevenue].Id).ShouldBe(-200m);
        fixture.Net(fixture.OutputVat.Id).ShouldBe(-10m);
    }

    [Fact]
    public async Task The_cost_of_the_sale_is_stated_once_by_the_issue_alone()
    {
        // The two journals together: the sale's, which never mentions cost, and the
        // issue's, which debits cost of goods sold and credits inventory. Stating the
        // cost in both would report the margin as double what the firm made.
        SalesPostingFixture fixture = new(onHand: 100m, unitCost: 60m);
        fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        fixture.Journals.Count.ShouldBe(2);
        fixture.Net(fixture.AccountLedgers[StockAccount.CostOfGoodsSold].Id).ShouldBe(120m);
        fixture.Net(fixture.AccountLedgers[StockAccount.Inventory].Id).ShouldBe(-120m);
    }

    [Fact]
    public async Task Every_journal_the_sale_raises_balances()
    {
        SalesPostingFixture fixture = new();
        fixture.Draft(quantity: 3m, rate: 33.33m, tax: 5m);

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        foreach (Voucher journal in fixture.Journals)
        {
            journal.Status.ShouldBe(VoucherStatus.Posted);

            journal.Lines.Where(line => line.Side == EntrySide.Debit)
                .Sum(line => line.Amount.Amount)
                .ShouldBe(
                    journal.Lines.Where(line => line.Side == EntrySide.Credit)
                        .Sum(line => line.Amount.Amount));
        }
    }

    [Fact]
    public async Task A_sale_of_goods_the_warehouse_does_not_hold_moves_nothing_at_all()
    {
        // The refusal that matters most here. If the stock check happened after the bill
        // were raised, an overselling firm would accrue debts for goods it never shipped.
        SalesPostingFixture fixture = new(onHand: 1m);
        SalesInvoice invoice = fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        Result<PostSalesInvoiceResponse> result = await fixture.Post();

        result.IsFailure.ShouldBeTrue();
        fixture.Raised.ShouldBeEmpty();
        fixture.Issued.ShouldBeEmpty();
        invoice.BillId.ShouldBeNull();
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_tax_head_with_no_account_stops_the_sale_before_it_is_saved()
    {
        SalesPostingFixture fixture = new(taxAccountAssigned: false);
        fixture.Draft(quantity: 2m, rate: 100m, tax: 10m);

        Result<PostSalesInvoiceResponse> result = await fixture.Post();

        result.Error.Code.ShouldBe("TaxAccounts.NotConfigured");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_customer_s_own_terms_decide_when_the_bill_falls_due()
    {
        SalesPostingFixture fixture = new();
        fixture.Draft();

        (await fixture.Post()).IsSuccess.ShouldBeTrue();

        // Thirty days, from the ledger rather than from the invoice.
        fixture.Raised.ShouldHaveSingleItem().DueDate
            .ShouldBe(SalesPostingFixture.InvoiceDate.AddDays(30));
    }

    [Fact]
    public async Task Terms_stated_on_the_command_override_the_ledger()
    {
        SalesPostingFixture fixture = new();
        fixture.Draft();

        (await fixture.Post(creditDays: 7)).IsSuccess.ShouldBeTrue();

        fixture.Raised.ShouldHaveSingleItem().DueDate
            .ShouldBe(SalesPostingFixture.InvoiceDate.AddDays(7));
    }

    [Fact]
    public async Task An_invoice_that_has_already_posted_cannot_post_again()
    {
        SalesPostingFixture fixture = new();
        fixture.Draft(post: true);

        Result<PostSalesInvoiceResponse> result = await fixture.Post();

        result.Error.Code.ShouldBe("SalesInvoice.AlreadyPosted");
        fixture.Issued.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invoice_of_another_firm_is_not_found_rather_than_posted()
    {
        SalesPostingFixture fixture = new();
        fixture.Draft();
        fixture.Holds(null);

        (await fixture.Post()).Error.Code.ShouldBe("SalesInvoice.NotFound");
    }

    [Fact]
    public async Task Nothing_posts_until_a_firm_and_branch_are_selected()
    {
        // The journal and the issue both belong to a branch, so there is nothing
        // sensible to do without one.
        SalesPostingFixture fixture = new(firmSelected: false);
        fixture.Draft();

        (await fixture.Post()).Error.Code.ShouldBe("SalesInvoice.NoFirmOrBranchSelected");
    }

    [Fact]
    public async Task The_issue_is_numbered_from_a_series_of_its_own()
    {
        // So a stock ledger tells goods that went to a customer from goods that went to
        // a department.
        SalesPostingFixture fixture = new();
        fixture.Draft();

        Result<PostSalesInvoiceResponse> result = await fixture.Post();

        result.Value.StockDocumentNumber.ShouldStartWith("SI");
        result.Value.Number.ShouldBe("SL/2026/0001");
    }
}
