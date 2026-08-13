using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Purchase;

/// <summary>Tests for <see cref="PurchaseInvoice"/>: the document goods arrive on.</summary>
/// <remarks>
/// Most of what this checks is the same as a sale's, because the two documents are
/// deliberately the same shape. What is worth reading is the handful of places they
/// differ: a purchase names batches and serial numbers that do not exist yet, and it
/// carries the supplier's own invoice number because that is what a reclaim is made
/// against.
/// </remarks>
public sealed class PurchaseInvoiceTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId FirmKey = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly CurrencyCode Kwd = CurrencyCode.FromTrusted("KWD");
    private static readonly DateOnly Date = new(2026, 8, 13);
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly FinancialYear Year = FinancialYear.Create(
        Tenant, FirmKey, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [])
        .Value;

    // ------------------------------------------------------------------ the party

    [Fact]
    public void A_purchase_from_somebody_who_is_not_a_supplier_is_refused()
    {
        // The party is the one thing on a purchase nobody re-reads before posting, and a
        // purchase booked against a bank account would sit in the creditors report for
        // ever.
        Result<PurchaseInvoice> drafted = Draft(party: PartyNamed(LedgerKind.Customer));

        drafted.Error.Code.ShouldBe("PurchaseInvoice.NotASupplier");
    }

    [Fact]
    public void A_supplier_withdrawn_from_use_takes_no_more_orders()
    {
        Ledger supplier = PartyNamed(LedgerKind.Supplier);
        supplier.Deactivate();

        Draft(party: supplier).Error.Code.ShouldBe("PurchaseInvoice.SupplierWithdrawn");
    }

    // ------------------------------------------------------------------ which way it runs

    [Fact]
    public void Only_a_return_may_name_the_purchase_it_is_against()
    {
        // A document confused about what it is, and the confusion would reach the
        // accounts: the posting reads the kind to decide which way the goods move.
        Result<PurchaseInvoice> drafted = Draft(
            kind: PurchaseDocumentKind.Invoice, returns: PurchaseInvoiceId.NewId());

        drafted.Error.Code.ShouldBe("PurchaseInvoice.NotAReturn");
    }

    [Fact]
    public void A_return_may_be_raised_without_naming_one()
    {
        // Goods go back to a supplier without the original paperwork to hand often
        // enough that refusing would leave a storekeeper unable to record what has just
        // left the yard.
        PurchaseInvoice note = Draft(kind: PurchaseDocumentKind.Return).Value;

        note.IsReturn.ShouldBeTrue();
        note.ReturnsInvoiceId.ShouldBeNull();
    }

    [Fact]
    public void A_return_line_is_entered_positive_like_every_other_line()
    {
        PurchaseInvoice note = Draft(kind: PurchaseDocumentKind.Return).Value;

        Result<PurchaseInvoiceLine> line = AddGoods(note, quantity: -3m, rate: 100m);

        line.Error.Code.ShouldBe("PurchaseInvoice.QuantityNotPositive");
    }

    // ------------------------------------------------------------------ batches and serials

    [Fact]
    public void A_batched_product_needs_the_batch_the_goods_arrived_in()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Batched();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 10m, 10m, 5m, Assessed(50m, 0m));

        line.Error.Code.ShouldBe("PurchaseInvoice.BatchRequired");
    }

    [Fact]
    public void The_batch_is_a_number_the_supplier_printed_rather_than_one_on_a_shelf()
    {
        // The difference from a sale that matters. A sale selects a batch that exists; a
        // purchase is usually the moment one comes into existence, so the line carries
        // what the supplier printed and the receipt opens it.
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Batched();

        PurchaseInvoiceLine line = invoice.AddLine(
            product, unit, 10m, 10m, 5m, Assessed(50m, 0m),
            batchNumber: "LOT-4471", expiresOn: new DateOnly(2027, 6, 30)).Value;

        line.BatchNumber.ShouldBe("LOT-4471");
        line.ExpiresOn.ShouldBe(new DateOnly(2027, 6, 30));
    }

    [Fact]
    public void A_batch_number_on_a_product_that_is_not_batched_is_refused()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Plain();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 1m, 1m, 100m, Assessed(100m, 0m), batchNumber: "LOT-1");

        line.Error.Code.ShouldBe("PurchaseInvoice.BatchNotTracked");
    }

    [Fact]
    public void An_expiry_date_with_no_batch_to_belong_to_is_refused()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Plain();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 1m, 1m, 100m, Assessed(100m, 0m),
            expiresOn: new DateOnly(2027, 1, 1));

        line.Error.Code.ShouldBe("PurchaseInvoice.ExpiryWithoutBatch");
    }

    [Fact]
    public void A_serialised_product_needs_one_number_for_every_unit_arriving()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Serialised();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 3m, 3m, 100m, Assessed(300m, 0m),
            serialNumbers: ["SN-1", "SN-2"]);

        line.Error.Code.ShouldBe("PurchaseInvoice.SerialCountMismatch");
    }

    [Fact]
    public void The_same_serial_number_twice_on_one_line_is_refused()
    {
        // Two boxes with the same number on them is a number that identifies neither,
        // and the receipt would refuse the second after the first had already gone in.
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Serialised();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 2m, 2m, 100m, Assessed(200m, 0m),
            serialNumbers: ["SN-1", "sn-1"]);

        line.Error.Code.ShouldBe("PurchaseInvoice.SerialRepeated");
    }

    [Fact]
    public void The_numbers_arriving_are_kept_against_the_line_that_brought_them_in()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Serialised();

        PurchaseInvoiceLine line = invoice.AddLine(
            product, unit, 2m, 2m, 100m, Assessed(200m, 0m),
            serialNumbers: ["SN-1", "SN-2"]).Value;

        line.Serials.Select(serial => serial.SerialNumber)
            .ShouldBe(["SN-1", "SN-2"]);
    }

    // ------------------------------------------------------------------ what it comes to

    [Fact]
    public void The_tax_has_to_match_the_line_it_was_assessed_against()
    {
        // A mismatch means the rate or the discount changed after the tax was computed,
        // which is input tax reclaimed against a figure the document contradicts.
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Plain();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 2m, 2m, 100m, Assessed(150m, 7.5m));

        line.Error.Code.ShouldBe("PurchaseInvoice.TaxNotForThisLine");
    }

    [Fact]
    public void A_discount_bigger_than_the_line_it_comes_off_is_refused()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Plain();

        Result<PurchaseInvoiceLine> line = invoice.AddLine(
            product, unit, 1m, 1m, 100m, Assessed(0m, 0m), discount: 150m);

        line.Error.Code.ShouldBe("PurchaseInvoice.DiscountExceedsLine");
    }

    [Fact]
    public void A_dinar_purchase_is_rounded_to_the_three_places_a_dinar_has()
    {
        // Not to two. A Kuwaiti, Bahraini or Omani supplier bills in thousandths, and a
        // total rounded to hundredths quietly disputes a fils on every invoice whose
        // third decimal is not a zero.
        PurchaseInvoice invoice = Draft(currency: Kwd).Value;
        (Product product, UnitOfMeasure unit) = Plain();

        invoice.AddLine(
            product, unit, 1m, 1m, 10.123m, Assessed(10.123m, 0m, Kwd))
            .IsSuccess.ShouldBeTrue();

        invoice.Total.Amount.ShouldBe(10.123m);
        invoice.RoundingDifference.Amount.ShouldBe(0m);
    }

    [Fact]
    public void A_line_priced_finer_than_its_currency_is_refused_rather_than_quietly_rounded()
    {
        // Why the rounding difference is nil on every purchase today, exactly as it is on
        // every sale: the engine returns the taxable amount at the currency's scale, and a
        // line whose price implies more precision than that no longer agrees with it.
        // Refusing is right - the alternative prints a total its own lines contradict -
        // but it does mean Round Off is a posting nothing currently produces.
        PurchaseInvoice invoice = Draft(currency: Kwd).Value;
        (Product product, UnitOfMeasure unit) = Plain();

        invoice.AddLine(
            product, unit, 1m, 1m, 10.1235m, Assessed(10.1235m, 0m, Kwd))
            .Error.Code.ShouldBe("PurchaseInvoice.TaxNotForThisLine");
    }

    [Fact]
    public void A_charge_mapped_to_another_kind_of_document_is_refused()
    {
        PurchaseInvoice invoice = Draft().Value;

        Result<PurchaseInvoiceCharge> charge = invoice.AddCharge(
            Charge(ChargeableDocument.Sales, isAddition: true), 30m);

        charge.Error.Code.ShouldBe("PurchaseInvoice.ChargeNotForPurchases");
    }

    [Fact]
    public void The_same_charge_twice_is_refused_rather_than_added_twice()
    {
        PurchaseInvoice invoice = Draft().Value;
        AdditionalLedger freight = Charge(ChargeableDocument.Purchase, isAddition: true);

        invoice.AddCharge(freight, 30m).IsSuccess.ShouldBeTrue();

        invoice.AddCharge(freight, 20m).Error.Code.ShouldBe("PurchaseInvoice.ChargeRepeated");
    }

    // ------------------------------------------------------------------ the supplier's own document

    [Fact]
    public void The_suppliers_invoice_number_and_date_are_both_kept()
    {
        // Not a convenience, unlike a sale's reference: input tax is only reclaimable
        // against a tax invoice the supplier issued, and a return reports the supplier's
        // number and date rather than whatever the firm numbered its own entry.
        PurchaseInvoice invoice = Draft().Value;

        invoice.SetSupplierDocument(
            " INV-8842 ", new DateOnly(2026, 8, 11), "Delivered short").IsSuccess.ShouldBeTrue();

        invoice.SupplierInvoiceNumber.ShouldBe("INV-8842");
        invoice.SupplierInvoiceDate.ShouldBe(new DateOnly(2026, 8, 11));
        invoice.Narration.ShouldBe("Delivered short");
    }

    [Fact]
    public void A_supplier_date_with_no_number_to_belong_to_is_refused()
    {
        // On its own it is a fact about a document nobody can identify, and it would
        // reach a return as a reclaim against an invoice that cannot be produced.
        PurchaseInvoice invoice = Draft().Value;

        Result set = invoice.SetSupplierDocument(null, new DateOnly(2026, 8, 11), null);

        set.Error.Code.ShouldBe("PurchaseInvoice.SupplierNumberRequired");
    }

    // ------------------------------------------------------------------ posting

    [Fact]
    public void A_purchase_with_nothing_on_it_does_not_post()
    {
        PurchaseInvoice invoice = Draft().Value;

        invoice.Post(User, Now).Error.Code.ShouldBe("PurchaseInvoice.NoLines");
    }

    [Fact]
    public void A_purchase_that_comes_to_nothing_does_not_post()
    {
        PurchaseInvoice invoice = Draft().Value;
        (Product product, UnitOfMeasure unit) = Plain();

        invoice.AddLine(
            product, unit, 1m, 1m, 100m, Assessed(0m, 0m), discount: 100m)
            .IsSuccess.ShouldBeTrue();

        invoice.Post(User, Now).Error.Code.ShouldBe("PurchaseInvoice.NothingToBill");
    }

    [Fact]
    public void A_posted_purchase_can_no_longer_be_changed()
    {
        PurchaseInvoice invoice = Posted();
        (Product product, UnitOfMeasure unit) = Plain();

        invoice.AddLine(product, unit, 1m, 1m, 100m, Assessed(100m, 0m))
            .Error.Code.ShouldBe("PurchaseInvoice.NotEditable");
        invoice.SetSupplierDocument("INV-1", null, null)
            .Error.Code.ShouldBe("PurchaseInvoice.NotEditable");
    }

    [Fact]
    public void What_the_posting_produced_is_named_once_and_only_once()
    {
        // A purchase that named them twice would be claiming two receipts or two debts
        // for one delivery.
        PurchaseInvoice invoice = Posted();

        invoice.RecordPosting(StockDocumentId.NewId(), BillId.NewId(), VoucherId.NewId())
            .IsSuccess.ShouldBeTrue();

        invoice.RecordPosting(StockDocumentId.NewId(), BillId.NewId(), VoucherId.NewId())
            .Error.Code.ShouldBe("PurchaseInvoice.AlreadyRecorded");
    }

    [Fact]
    public void A_draft_has_produced_nothing_to_record()
    {
        PurchaseInvoice invoice = Draft().Value;

        invoice.RecordPosting(StockDocumentId.NewId(), null, VoucherId.NewId())
            .Error.Code.ShouldBe("PurchaseInvoice.NotPosted");
    }

    [Fact]
    public void Cancelling_needs_a_posted_purchase_and_a_reason()
    {
        PurchaseInvoice draft = Draft().Value;
        draft.Cancel("Entered against the wrong supplier")
            .Error.Code.ShouldBe("PurchaseInvoice.NotPosted");

        PurchaseInvoice invoice = Posted();
        invoice.Cancel("  ").Error.Code.ShouldBe("PurchaseInvoice.CancellationReasonRequired");

        invoice.Cancel("Entered twice").IsSuccess.ShouldBeTrue();
        invoice.Status.ShouldBe(PurchaseInvoiceStatus.Cancelled);
        invoice.CancellationReason.ShouldBe("Entered twice");
    }

    // ------------------------------------------------------------------ scaffolding

    private static Result<PurchaseInvoice> Draft(
        Ledger? party = null,
        CurrencyCode? currency = null,
        PurchaseDocumentKind kind = PurchaseDocumentKind.Invoice,
        PurchaseInvoiceId? returns = null) =>
        PurchaseInvoice.CreateDraft(
            Tenant,
            FirmKey,
            Branch,
            Year,
            "PU/2026/0001",
            Date,
            party ?? PartyNamed(LedgerKind.Supplier),
            Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
            TaxMode.Tax,
            currency ?? Qar,
            kind,
            returns);

    private static PurchaseInvoice Posted()
    {
        PurchaseInvoice invoice = Draft().Value;
        AddGoods(invoice, 1m, 100m).IsSuccess.ShouldBeTrue();
        invoice.Post(User, Now).IsSuccess.ShouldBeTrue();

        return invoice;
    }

    private static Result<PurchaseInvoiceLine> AddGoods(
        PurchaseInvoice invoice,
        decimal quantity,
        decimal rate)
    {
        (Product product, UnitOfMeasure unit) = Plain();

        return invoice.AddLine(
            product, unit, quantity, quantity, rate, Assessed(quantity * rate, 0m));
    }

    private static TaxAssessment Assessed(
        decimal taxable,
        decimal tax,
        CurrencyCode? currency = null) =>
        TaxCalculator.Calculate(
            Money.Of(taxable, currency ?? Qar),
            TaxRate.FromTrusted(
                taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
            new TaxContext(
                TaxRegime.GccVat,
                DocumentTaxMode.Taxable,
                AmountsIncludeTax: false,
                IsInterStateSupply: false));

    private static (Product Product, UnitOfMeasure Unit) Plain() => Made(product => product);

    private static (Product Product, UnitOfMeasure Unit) Batched() =>
        Made(product =>
        {
            product.SetTracking(tracksBatches: true, tracksSerialNumbers: false)
                .IsSuccess.ShouldBeTrue();
            return product;
        });

    private static (Product Product, UnitOfMeasure Unit) Serialised() =>
        Made(product =>
        {
            product.SetTracking(tracksBatches: false, tracksSerialNumbers: true)
                .IsSuccess.ShouldBeTrue();
            return product;
        });

    private static (Product Product, UnitOfMeasure Unit) Made(Func<Product, Product> arrange)
    {
        UnitOfMeasure unit = UnitOfMeasure.CreateBase(Tenant, FirmKey, "EACH", "Each").Value;

        Product product = Product.Create(
            Category.CreateRoot(Tenant, FirmKey, "GEN", "General").Value,
            unit,
            $"PRO-{Guid.NewGuid():N}"[..12],
            "A thing",
            ItemType.Stock,
            Qar).Value;

        return (arrange(product), unit);
    }

    private static AdditionalLedger Charge(ChargeableDocument document, bool isAddition) =>
        AdditionalLedger.Map(
            Tenant,
            FirmKey,
            document,
            LedgerNamed("FREIGHT", LedgerKind.AdditionalCharge),
            isAddition).Value;

    private static Ledger PartyNamed(LedgerKind kind) =>
        LedgerNamed(kind == LedgerKind.Supplier ? "2200" : "1200", kind);

    private static Ledger LedgerNamed(string code, LedgerKind kind) =>
        Ledger.Create(
            AccountGroup.CreateRoot(
                Tenant, FirmKey, $"G{code}", $"Group {code}", AccountNature.Liability).Value,
            code,
            code,
            kind,
            Qar).Value;
}
