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
    public void The_total_is_rounded_once_and_the_difference_is_kept()
    {
        // The business's answer: tax stays at full precision per component, and only the
        // total is rounded - the difference going to Round Off rather than being lost.
        SalesInvoice invoice = Draft().Value;
        UnitOfMeasure each = BaseUnit();

        invoice.AddLine(Stocked(each), each, 3m, 3m, 33.333m, Assessed(99.999m, 5.0004m));

        // Rounded once, at the end, and the difference kept rather than lost: whatever
        // the engine made of the line, the total is the gross rounded to the currency.
        invoice.Total.Amount.ShouldBe(
            decimal.Round(invoice.GrossTotal.Amount, 2, MidpointRounding.AwayFromZero));
        invoice.RoundingDifference.Amount.ShouldBe(
            invoice.Total.Amount - invoice.GrossTotal.Amount);
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

    // ------------------------------------------------------------------ helpers

    /// <summary>Assesses a line the way the application layer will: through the engine.</summary>
    /// <remarks>
    /// The percentage is derived from the two figures the test wants rather than stated,
    /// so a test reads as "this line comes to a thousand and carries fifty of tax" - which
    /// is what the invoice is being asked about - rather than as a rate somebody has to
    /// multiply out to check.
    /// </remarks>
    private static TaxAssessment Assessed(decimal taxable, decimal tax) =>
        TaxCalculator.Calculate(
            Money.Of(taxable, Qar),
            TaxRate.FromTrusted(taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
            new TaxContext(TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false));

    private static Result<SalesInvoice> Draft(Ledger? customer = null, Warehouse? warehouse = null) =>
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
            Qar);

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
