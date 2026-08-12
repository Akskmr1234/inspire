using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Results;

namespace ERP.Application.Tests.Sales;

/// <summary>Tests for <see cref="CreateSalesInvoiceCommandHandler"/>.</summary>
/// <remarks>
/// Entering an invoice is where the tax engine meets the document. What these check is
/// that the engine is asked the right question - which regime, which mode, and whether the
/// supply crossed a state line - and that what it answered is what the invoice records,
/// because a reprint years later shows the tax that was charged rather than the tax
/// today's rates would produce.
/// </remarks>
public sealed class CreateSalesInvoiceTests
{
    [Fact]
    public async Task An_invoice_is_numbered_totalled_and_left_as_a_draft()
    {
        SalesPostingFixture fixture = new();

        Result<SalesInvoiceResponse> result = await fixture.Create(
            quantity: 2m, rate: 100m, taxPercentage: 5m);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);

        // A draft has moved nothing. That is the whole point of posting being separate.
        result.Value.Status.ShouldBe(SalesInvoiceStatus.Draft);
        result.Value.Number.ShouldStartWith("SL");
        result.Value.Taxable.ShouldBe(200m);
        result.Value.Tax.ShouldBe(10m);
        result.Value.Total.ShouldBe(210m);

        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_VAT_firm_charges_one_head()
    {
        SalesPostingFixture fixture = new();

        await fixture.Create(quantity: 2m, rate: 100m, taxPercentage: 5m);

        SalesInvoiceLine line = fixture.Created.Lines.ShouldHaveSingleItem();

        line.Components.ShouldHaveSingleItem().Type.ShouldBe(TaxComponentType.Vat);
        line.TaxAmount.Amount.ShouldBe(10m);
    }

    [Fact]
    public async Task A_GST_firm_selling_within_its_own_state_charges_CGST_and_SGST()
    {
        SalesPostingFixture fixture = new(
            regime: TaxRegime.IndiaGst, firmState: "KL", customerState: "KL");

        await fixture.Create(quantity: 1m, rate: 1_000m, taxPercentage: 18m);

        IReadOnlyList<SalesInvoiceLineTax> heads =
            fixture.Created.Lines.ShouldHaveSingleItem().Components;

        heads.Count.ShouldBe(2);
        heads.Sum(head => head.Amount).ShouldBe(180m);
        heads.ShouldContain(head => head.Type == TaxComponentType.Cgst && head.Amount == 90m);
        heads.ShouldContain(head => head.Type == TaxComponentType.Sgst && head.Amount == 90m);
    }

    [Fact]
    public async Task A_GST_firm_selling_across_a_state_line_charges_IGST_instead()
    {
        // The comparison the whole place-of-supply rule turns on, and the one thing on an
        // invoice that a firm cannot correct after filing.
        SalesPostingFixture fixture = new(
            regime: TaxRegime.IndiaGst, firmState: "KL", customerState: "TN");

        await fixture.Create(quantity: 1m, rate: 1_000m, taxPercentage: 18m);

        SalesInvoiceLineTax head =
            fixture.Created.Lines.ShouldHaveSingleItem().Components.ShouldHaveSingleItem();

        head.Type.ShouldBe(TaxComponentType.Igst);
        head.Amount.ShouldBe(180m);
    }

    [Fact]
    public async Task A_customer_whose_state_nobody_recorded_is_treated_as_local()
    {
        // The safer of the two readings: it keeps the tax in the state the firm is
        // registered in, which is recoverable, rather than charging IGST that is not.
        SalesPostingFixture fixture = new(
            regime: TaxRegime.IndiaGst, firmState: "KL", customerState: null);

        await fixture.Create(quantity: 1m, rate: 1_000m, taxPercentage: 18m);

        fixture.Created.Lines.ShouldHaveSingleItem().Components
            .ShouldNotContain(head => head.Type == TaxComponentType.Igst);
    }

    [Fact]
    public async Task A_discount_comes_off_the_line_before_the_tax_is_worked_out()
    {
        SalesPostingFixture fixture = new();

        Result<SalesInvoiceResponse> result = await fixture.Create(
            quantity: 2m, rate: 100m, taxPercentage: 5m, discount: 20m);

        result.Value.Taxable.ShouldBe(180m);
        result.Value.Tax.ShouldBe(9m);
        result.Value.Total.ShouldBe(189m);
    }

    [Fact]
    public async Task A_charge_adds_or_deducts_as_the_firm_s_matrix_says()
    {
        SalesPostingFixture fixture = new();

        Result<SalesInvoiceResponse> result = await fixture.Create(
            quantity: 2m,
            rate: 100m,
            taxPercentage: 5m,
            charges:
            [
                new SalesInvoiceChargeInput(fixture.Freight.LedgerId.Value, 30m),
                new SalesInvoiceChargeInput(fixture.DiscountAllowed.LedgerId.Value, 20m),
            ]);

        result.Value.ChargeTotal.ShouldBe(10m);
        result.Value.Total.ShouldBe(220m);
    }

    [Fact]
    public async Task A_charge_this_firm_does_not_carry_on_a_sale_is_refused()
    {
        SalesPostingFixture fixture = new();

        Result<SalesInvoiceResponse> result = await fixture.Create(
            charges: [new SalesInvoiceChargeInput(Guid.NewGuid(), 30m)]);

        result.Error.Code.ShouldBe("SalesInvoice.ChargeNotMapped");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_entered_until_a_firm_and_branch_are_selected()
    {
        SalesPostingFixture fixture = new(firmSelected: false);

        (await fixture.Create()).Error.Code.ShouldBe("SalesInvoice.NoFirmOrBranchSelected");
    }

    [Fact]
    public async Task An_invoice_entered_can_then_be_posted()
    {
        // The whole counter flow through both handlers: type it, then post it.
        SalesPostingFixture fixture = new();

        (await fixture.Create(quantity: 2m, rate: 100m, taxPercentage: 5m))
            .IsSuccess.ShouldBeTrue();

        Result<PostSalesInvoiceResponse> posted = await fixture.Post();

        posted.IsSuccess.ShouldBeTrue(posted.IsFailure ? posted.Error.Description : null);
        posted.Value.Total.ShouldBe(210m);
        fixture.Created.Status.ShouldBe(SalesInvoiceStatus.Posted);
        fixture.Raised.ShouldHaveSingleItem().OriginalAmount.Amount.ShouldBe(210m);
    }
}
