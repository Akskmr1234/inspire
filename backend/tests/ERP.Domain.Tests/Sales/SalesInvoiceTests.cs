using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Sales;

/// <summary>Tests for <see cref="SalesInvoice"/>: what is sold, and what is owed.</summary>
/// <remarks>
/// The invoice owns what may be entered; the handler owns what happens when it posts.
/// These cover the first half, and in particular the two things a printed invoice cannot
/// survive getting wrong: totals that do not add up, and a tax figure computed against
/// something other than the line it sits on.
/// </remarks>
public sealed class SalesInvoiceTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateOnly Date = new(2026, 8, 10);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_invoice_is_billed_to_a_customer_and_no_other_kind_of_account()
    {
        // A sale to a bank account or an expense head would sit in the debtors report
        // for ever, and the party is the one thing nobody re-reads before posting.
        Draft(customer: PartyIn(Firm, LedgerKind.Bank)).Error.Code
            .ShouldBe("SalesInvoice.NotACustomer");

        Draft().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_withdrawn_customer_or_warehouse_cannot_be_used()
    {
        Ledger closed = PartyIn(Firm, LedgerKind.Customer);
        closed.Deactivate();

        Draft(customer: closed).Error.Code.ShouldBe("SalesInvoice.CustomerWithdrawn");

        Warehouse shut = Godown();
        shut.Deactivate();

        Draft(warehouse: shut).Error.Code.ShouldBe("SalesInvoice.WarehouseWithdrawn");
    }

    [Fact]
    public void A_line_carries_the_tax_that_was_assessed_against_it()
    {
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        SalesInvoiceLine line = invoice
            .AddLine(Stocked(each), each, 10m, 10m, 100m, Assessed(1_000m, 50m)).Value;

        line.TaxableAmount.Amount.ShouldBe(1_000m);
        line.TaxAmount.Amount.ShouldBe(50m);
        line.LineTotal.Amount.ShouldBe(1_050m);
        line.LineNumber.ShouldBe(1);
    }

    [Fact]
    public void Tax_assessed_against_a_different_amount_is_refused()
    {
        // The rate or the discount changed after the tax was computed. Accepting it
        // would print an invoice whose own figures do not add up.
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, 10m, 10m, 100m, Assessed(900m, 45m))
            .Error.Code.ShouldBe("SalesInvoice.TaxNotForThisLine");
    }

    [Fact]
    public void A_discount_comes_off_before_tax_and_cannot_exceed_the_line()
    {
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();
        Product product = Stocked(each);

        invoice.AddLine(product, each, 10m, 10m, 100m, Assessed(900m, 45m), discount: 100m)
            .IsSuccess.ShouldBeTrue();

        invoice.AddLine(Stocked(each), each, 1m, 1m, 50m, Assessed(0m, 0m), discount: 60m)
            .Error.Code.ShouldBe("SalesInvoice.DiscountExceedsLine");
    }

    [Fact]
    public void A_negative_quantity_is_a_return_rather_than_a_sale()
    {
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, -1m, -1m, 100m, Assessed(-100m, 0m))
            .Error.Code.ShouldBe("SalesInvoice.QuantityNotPositive");
    }

    [Fact]
    public void A_charge_is_entered_positive_and_the_matrix_decides_its_direction()
    {
        // A freight of minus fifty and a discount of plus fifty both read as somebody
        // fighting the form, and one of them is silently wrong on the total.
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();
        invoice.AddLine(Stocked(each), each, 10m, 10m, 100m, Assessed(1_000m, 50m));

        invoice.AddCharge(Charge("FREIGHT", isAddition: true), 75m).IsSuccess.ShouldBeTrue();
        invoice.AddCharge(Charge("DISC", isAddition: false), 25m).IsSuccess.ShouldBeTrue();

        invoice.ChargeTotal.Amount.ShouldBe(50m);
        invoice.Total.Amount.ShouldBe(1_100m);

        invoice.AddCharge(Charge("FREIGHT2", isAddition: true), -10m)
            .Error.Code.ShouldBe("SalesInvoice.ChargeNotPositive");
    }

    [Fact]
    public void The_same_charge_cannot_be_added_twice()
    {
        SalesInvoice invoice = Draft().Value;
        AdditionalLedger freight = Charge("FREIGHT", isAddition: true);

        invoice.AddCharge(freight, 50m).IsSuccess.ShouldBeTrue();
        invoice.AddCharge(freight, 25m).Error.Code.ShouldBe("SalesInvoice.ChargeRepeated");
    }

    [Fact]
    public void The_total_is_rounded_once_at_the_end()
    {
        // The business's answer: tax stays per component, and only the total is rounded -
        // the difference going to Round Off rather than being lost.
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, 3m, 3m, 33.34m, Assessed(100.02m, 5.0004m))
            .IsSuccess.ShouldBeTrue();

        invoice.Total.ShouldBe(invoice.GrossTotal.Rounded());
        invoice.RoundingDifference.Amount.ShouldBe(
            invoice.Total.Amount - invoice.GrossTotal.Amount);
    }

    [Fact]
    public void A_dinar_invoice_is_rounded_to_the_three_places_a_dinar_has()
    {
        // Not to two. A Kuwaiti, Bahraini or Omani firm bills in thousandths, and a
        // total rounded to hundredths quietly gives away a fils on every invoice whose
        // third decimal is not a zero - in the Gulf market this product is built for.
        SalesInvoice invoice = Draft(currency: CurrencyCode.FromTrusted("KWD")).Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(
            Stocked(each), each, 1m, 1m, 10.123m,
            Assessed(10.123m, 0m, CurrencyCode.FromTrusted("KWD"))).IsSuccess.ShouldBeTrue();

        invoice.Total.Amount.ShouldBe(10.123m);
        invoice.RoundingDifference.Amount.ShouldBe(0m);
    }

    [Fact]
    public void A_line_priced_finer_than_its_currency_is_refused_rather_than_quietly_rounded()
    {
        // Why the rounding difference is nil on every invoice today: the engine returns
        // the taxable amount at the currency's scale, and a line whose price implies
        // more precision than that no longer agrees with it. Refusing is right - the
        // alternative prints a total its own lines contradict - but it does mean Round
        // Off is a posting nothing currently produces.
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, 3m, 3m, 33.333m, Assessed(99.999m, 5.0004m))
            .Error.Code.ShouldBe("SalesInvoice.TaxNotForThisLine");
    }

    [Fact]
    public void An_invoice_that_comes_to_nothing_cannot_be_posted()
    {
        SalesInvoice invoice = Draft().Value;

        invoice.Post(User, Now).Error.Code.ShouldBe("SalesInvoice.NoLines");

        UnitOfMeasure each = BaseUnit();
        invoice.AddLine(Stocked(each), each, 1m, 1m, 100m, Assessed(0m, 0m), discount: 100m);

        invoice.Post(User, Now).Error.Code.ShouldBe("SalesInvoice.NothingToBill");
    }

    [Fact]
    public void A_posted_invoice_is_closed_to_further_change()
    {
        SalesInvoice invoice = Posted();
        UnitOfMeasure each = BaseUnit();

        invoice.IsEditable.ShouldBeFalse();
        invoice.PostedBy.ShouldBe(User);

        invoice.AddLine(Stocked(each), each, 1m, 1m, 10m, Assessed(10m, 0m))
            .Error.Code.ShouldBe("SalesInvoice.NotEditable");
        invoice.SetDetails("PO-1", null).Error.Code.ShouldBe("SalesInvoice.NotEditable");
        invoice.Post(User, Now).Error.Code.ShouldBe("SalesInvoice.AlreadyPosted");
    }

    [Fact]
    public void Only_a_posted_invoice_can_be_cancelled_and_only_with_a_reason()
    {
        Draft().Value.Cancel("wrong customer").Error.Code.ShouldBe("SalesInvoice.NotPosted");

        SalesInvoice posted = Posted();

        posted.Cancel("  ").Error.Code.ShouldBe("SalesInvoice.CancellationReasonRequired");
        posted.Cancel("Raised against the wrong customer").IsSuccess.ShouldBeTrue();
        posted.Status.ShouldBe(SalesInvoiceStatus.Cancelled);
    }

    [Fact]
    public void A_serialised_product_needs_one_number_for_every_unit_sold()
    {
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();
        Product handset = Stocked(each);
        handset.SetTracking(false, true);

        invoice.AddLine(handset, each, 2m, 2m, 900m, Assessed(1_800m, 90m))
            .Error.Code.ShouldBe("SalesInvoice.SerialCountMismatch");
    }

    [Fact]
    public void What_the_posting_produced_is_named_once_and_not_twice()
    {
        // The goods, the debt and the accounts: three things somebody reading the
        // invoice afterwards wants to reach, and none of them findable from it by any
        // other route.
        SalesInvoice invoice = Posted();

        StockDocumentId issue = StockDocumentId.NewId();
        BillId bill = BillId.NewId();
        VoucherId journal = VoucherId.NewId();

        invoice.RecordPosting(issue, bill, journal).IsSuccess.ShouldBeTrue();

        invoice.StockDocumentId.ShouldBe(issue);
        invoice.BillId.ShouldBe(bill);
        invoice.JournalVoucherId.ShouldBe(journal);

        // A second attempt would be an invoice claiming two issues or two debts for one
        // sale.
        invoice.RecordPosting(StockDocumentId.NewId(), BillId.NewId(), VoucherId.NewId())
            .Error.Code.ShouldBe("SalesInvoice.AlreadyRecorded");
    }

    [Fact]
    public void A_draft_has_produced_nothing_to_record()
    {
        Draft().Value
            .RecordPosting(StockDocumentId.NewId(), BillId.NewId(), VoucherId.NewId())
            .Error.Code.ShouldBe("SalesInvoice.NotPosted");
    }

    [Fact]
    public void A_document_is_a_sale_unless_it_says_otherwise()
    {
        // The default matters: every caller that predates returns, and every row that
        // predates the column, means an invoice.
        Draft().Value.Kind.ShouldBe(SalesDocumentKind.Invoice);
        Draft().Value.IsReturn.ShouldBeFalse();
    }

    [Fact]
    public void Only_a_return_may_name_the_invoice_it_is_against()
    {
        // An invoice claiming to return another invoice is confused about what it is,
        // and the confusion would reach the accounts - the posting reads the kind to
        // decide which way the goods and the money move.
        SalesInvoice sale = Posted();

        Draft(kind: SalesDocumentKind.Invoice, returns: sale.Id)
            .Error.Code.ShouldBe("SalesInvoice.NotAReturn");

        Draft(kind: SalesDocumentKind.Return, returns: sale.Id).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_return_need_not_name_an_invoice_at_all()
    {
        // Goods come back without their paperwork often enough that refusing would
        // leave a counter unable to record what is physically in front of them.
        SalesInvoice credit = Draft(kind: SalesDocumentKind.Return).Value;

        credit.IsReturn.ShouldBeTrue();
        credit.ReturnsInvoiceId.ShouldBeNull();
    }

    [Fact]
    public void A_return_carries_positive_quantities_like_any_other_document()
    {
        // The kind decides the direction, not the sign of the line. A negative quantity
        // would be a second spelling of the same fact, and every report would have to
        // normalise before it could sum.
        SalesInvoice credit = Draft(kind: SalesDocumentKind.Return).Value;
        UnitOfMeasure each = BaseUnit();

        credit.AddLine(Stocked(each), each, -2m, -2m, 100m, Assessed(200m, 10m))
            .Error.Code.ShouldBe("SalesInvoice.QuantityNotPositive");

        credit.AddLine(Stocked(each), each, 2m, 2m, 100m, Assessed(200m, 10m))
            .IsSuccess.ShouldBeTrue();

        credit.Total.Amount.ShouldBe(210m);
    }

    [Fact]
    public void An_expired_lot_cannot_be_sold()
    {
        // §10's last open gap, which the specification left for this document: the stock
        // position could never refuse expired goods, because an issue and a write-off both
        // have to be able to move them.
        SalesInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure each) = Batched();

        Result<SalesInvoiceLine> sold = invoice.AddLine(
            product, each, 2m, 2m, 100m, Assessed(200m, 10m),
            Lot(product, "OLD", expiresOn: Date.AddDays(-1)));

        sold.Error.Code.ShouldBe("SalesInvoice.BatchExpired");

        // And the message names the lot, its expiry and the day it was judged against,
        // because "expired" on a forty-line invoice is not something anybody can act on.
        sold.Error.Description.ShouldContain("OLD");
        sold.Error.Description.ShouldContain("2026-08-09");
    }

    [Fact]
    public void A_lot_expiring_on_the_day_of_the_sale_is_still_in_date()
    {
        // Expiry is the last day the goods are good, not the first day they are not.
        SalesInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure each) = Batched();

        invoice.AddLine(
            product, each, 2m, 2m, 100m, Assessed(200m, 10m),
            Lot(product, "TODAY", expiresOn: Date))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_lot_with_no_expiry_never_expires()
    {
        SalesInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure each) = Batched();

        invoice.AddLine(
            product, each, 2m, 2m, 100m, Assessed(200m, 10m),
            Lot(product, "KEEPS", expiresOn: null))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_expiry_is_judged_against_the_invoice_date_rather_than_today()
    {
        // A sale keyed a week late is measured against the day the goods actually went
        // out. Judging it against today would refuse a backdated invoice for a lot that
        // was in date when it was sold.
        SalesInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure each) = Batched();

        // Expires after this invoice's date, but well before any plausible "today".
        invoice.AddLine(
            product, each, 2m, 2m, 100m, Assessed(200m, 10m),
            Lot(product, "BACKDATED", expiresOn: Date.AddDays(1)))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_expired_lot_can_still_come_back_on_a_return()
    {
        // The whole reason this is a rule about selling rather than a check on the stock
        // movement. Goods a customer brings back have physically come back, and a lot that
        // expired while it sat on their shelf still has to reach the books - refusing it
        // would leave the firm unable to record what is standing in its yard.
        SalesInvoice credit = Draft(kind: SalesDocumentKind.Return).Value;
        (Product product, UnitOfMeasure each) = Batched();

        credit.AddLine(
            product, each, 2m, 2m, 100m, Assessed(200m, 10m),
            Lot(product, "OLD", expiresOn: Date.AddDays(-30)))
            .IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A batched product, and the unit it is stocked in.</summary>
    private static (Product Product, UnitOfMeasure Unit) Batched()
    {
        UnitOfMeasure each = BaseUnit();
        Product product = Stocked(each);

        product.SetTracking(tracksBatches: true, tracksSerialNumbers: false)
            .IsSuccess.ShouldBeTrue();

        return (product, each);
    }

    /// <summary>One lot of a batched product.</summary>
    private static Batch Lot(Product product, string number, DateOnly? expiresOn) =>
        Batch.Open(Tenant, Firm, product, number, manufacturedOn: null, expiresOn: expiresOn)
            .Value;

    /// <summary>Assesses a line the way the application layer will: through the engine.</summary>
    /// <remarks>
    /// The percentage is derived from the two figures the test wants rather than stated,
    /// so a test reads as "this line comes to a thousand and carries fifty of tax" - which
    /// is what the invoice is being asked about - rather than as a rate somebody has to
    /// multiply out to check.
    /// </remarks>
    private static TaxAssessment Assessed(
        decimal taxable,
        decimal tax,
        CurrencyCode? currency = null) =>
        TaxCalculator.Calculate(
            Money.Of(taxable, currency ?? Qar),
            TaxRate.FromTrusted(taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
            new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false));

    private static Result<SalesInvoice> Draft(
        Ledger? customer = null,
        Warehouse? warehouse = null,
        CurrencyCode? currency = null,
        SalesDocumentKind kind = SalesDocumentKind.Invoice,
        SalesInvoiceId? returns = null) =>
        SalesInvoice.CreateDraft(
            Tenant,
            Firm,
            Branch,
            Year(),
            "SL/2026/0001",
            Date,
            customer ?? PartyIn(Firm, LedgerKind.Customer),
            warehouse ?? Godown(),
            TaxMode.Tax,
            currency ?? Qar,
            kind,
            returns);

    private static SalesInvoice Posted()
    {
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, 10m, 10m, 100m, Assessed(1_000m, 50m));
        invoice.Post(User, Now).IsSuccess.ShouldBeTrue();

        return invoice;
    }

    private static FinancialYear Year() =>
        FinancialYear.Create(
            Tenant, Firm, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

    private static Warehouse Godown() =>
        Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;

    private static UnitOfMeasure BaseUnit() =>
        UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

    private static Product Stocked(UnitOfMeasure unit) =>
        Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            unit, $"PRO-{Guid.NewGuid():N}"[..12], "A thing", ItemType.Stock, Qar).Value;

    private static Ledger PartyIn(FirmId firmId, LedgerKind kind)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, firmId, "G2000", "Debtors", AccountNature.Asset).Value;

        return Ledger.Create(group, "2000", "Al Mansoor", kind, Qar).Value;
    }

    private static AdditionalLedger Charge(string code, bool isAddition)
    {
        AccountGroup group = AccountGroup.CreateRoot(
            Tenant, Firm, $"G{code}", $"Group {code}", AccountNature.Expense).Value;

        Ledger ledger = Ledger.Create(
            group, code, code, LedgerKind.AdditionalCharge, Qar).Value;

        return AdditionalLedger.Map(
            Tenant, Firm, ChargeableDocument.Sales, ledger, isAddition).Value;
    }
}
