using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Purchase;

/// <summary>Tests for <see cref="PurchaseOrder"/>: what was asked for, and what has arrived.</summary>
/// <remarks>
/// Most of the shape is a purchase invoice's and is tested there. What is worth reading here
/// is the one column an order adds - how much of each line has been invoiced - and the four
/// states it drives: a confirmed order fills across as many deliveries as it takes,
/// completes itself, reopens when a purchase is cancelled, and refuses to be invoiced past
/// what was ordered.
/// </remarks>
public sealed class PurchaseOrderTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId FirmKey = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateOnly Date = new(2026, 8, 15);
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static readonly FinancialYear Year = FinancialYear.Create(
        Tenant, FirmKey, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [])
        .Value;

    [Fact]
    public void An_order_placed_with_somebody_who_is_not_a_supplier_is_refused()
    {
        Draft(party: PartyNamed(LedgerKind.Customer))
            .Error.Code.ShouldBe("PurchaseOrder.NotASupplier");
    }

    [Fact]
    public void Goods_promised_before_they_were_ordered_is_refused()
    {
        // The mistake that would otherwise show up as an order overdue on the day it was
        // raised.
        Draft(expectedOn: Date.AddDays(-1))
            .Error.Code.ShouldBe("PurchaseOrder.ExpectedBeforeOrdered");
    }

    [Fact]
    public void An_order_with_nothing_on_it_cannot_be_confirmed()
    {
        PurchaseOrder order = Draft().Value;

        order.Confirm(User, Now).Error.Code.ShouldBe("PurchaseOrder.NoLines");
    }

    [Fact]
    public void A_confirmed_order_can_no_longer_be_changed()
    {
        PurchaseOrder order = Confirmed(10m, 50m);
        (Product product, UnitOfMeasure unit) = Made();

        order.AddLine(product, unit, 1m, 1m, 50m, Assessed(50m, 0m))
            .Error.Code.ShouldBe("PurchaseOrder.NotEditable");
    }

    [Fact]
    public void Nothing_can_be_invoiced_against_a_draft()
    {
        PurchaseOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        PurchaseOrderLine line = order
            .AddLine(product, unit, 10m, 10m, 50m, Assessed(500m, 0m)).Value;

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 1m })
            .Error.Code.ShouldBe("PurchaseOrder.NotOpen");
    }

    [Fact]
    public void Part_of_a_line_arrives_and_the_rest_stays_owed()
    {
        // The routine case on the purchase side rather than the exception: suppliers
        // part-ship considerably more often than customers part-collect.
        PurchaseOrder order = Confirmed(10m, 50m);
        PurchaseOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        line.InvoicedQuantity.ShouldBe(4m);
        line.OutstandingQuantity.ShouldBe(6m);
        line.IsFulfilled.ShouldBeFalse();

        // Still open, because the supplier still owes six.
        order.Status.ShouldBe(PurchaseOrderStatus.Confirmed);
        order.IsPartlyInvoiced.ShouldBeTrue();
    }

    [Fact]
    public void An_order_completes_itself_when_the_last_line_is_filled()
    {
        // Nobody has to remember to close it, which is the difference between a chase list
        // worth reading and one full of finished work.
        PurchaseOrder order = Confirmed(10m, 50m);
        PurchaseOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 6m })
            .IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.Confirmed);

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Completed);
        order.Lines[0].OutstandingQuantity.ShouldBe(0m);
    }

    [Fact]
    public void A_line_cannot_be_invoiced_for_more_than_was_ordered()
    {
        // A supplier who ships more than was ordered is a conversation somebody has to
        // have, not a figure the order should absorb silently.
        PurchaseOrder order = Confirmed(10m, 50m);
        PurchaseOrderLine line = order.Lines[0];

        Result over = order.RecordInvoiced(
            new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 11m });

        over.Error.Code.ShouldBe("PurchaseOrder.OverInvoiced");

        // And nothing was recorded, because the check runs before any of it is applied.
        line.InvoicedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void One_bad_line_leaves_every_other_line_untouched()
    {
        // The reason the quantities are checked in full before any are applied: an order
        // claiming goods arrived that no purchase carries is worse than a refusal.
        PurchaseOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        PurchaseOrderLine first = order
            .AddLine(product, unit, 5m, 5m, 10m, Assessed(50m, 0m)).Value;
        PurchaseOrderLine second = order
            .AddLine(product, unit, 5m, 5m, 10m, Assessed(50m, 0m)).Value;

        order.Confirm(User, Now).IsSuccess.ShouldBeTrue();

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal>
        {
            [first.Id] = 2m,
            [second.Id] = 99m,
        }).Error.Code.ShouldBe("PurchaseOrder.OverInvoiced");

        first.InvoicedQuantity.ShouldBe(0m);
        second.InvoicedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void A_completed_order_reopens_when_a_purchase_is_cancelled()
    {
        PurchaseOrder order = Confirmed(10m, 50m);
        PurchaseOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 10m })
            .IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.Completed);

        order.ReleaseInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(PurchaseOrderStatus.Confirmed);
        line.OutstandingQuantity.ShouldBe(4m);
    }

    [Fact]
    public void An_order_somebody_closed_deliberately_does_not_reopen()
    {
        // Reopening it would put an order back in front of a buyer who was told to stop
        // chasing it.
        PurchaseOrder order = Confirmed(10m, 50m);
        PurchaseOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();
        order.Close("The supplier discontinued the line").IsSuccess.ShouldBeTrue();

        order.ReleaseInvoiced(new Dictionary<PurchaseOrderLineId, decimal> { [line.Id] = 4m })
            .Error.Code.ShouldBe("PurchaseOrder.NotReopenable");
    }

    [Fact]
    public void A_closed_order_takes_no_more_purchases()
    {
        PurchaseOrder order = Confirmed(10m, 50m);

        order.Close("Bought elsewhere").IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        order.ClosureReason.ShouldBe("Bought elsewhere");

        order.RecordInvoiced(
            new Dictionary<PurchaseOrderLineId, decimal> { [order.Lines[0].Id] = 1m })
            .Error.Code.ShouldBe("PurchaseOrder.NotOpen");
    }

    [Fact]
    public void Closing_needs_a_reason_and_happens_once()
    {
        PurchaseOrder order = Confirmed(10m, 50m);

        order.Close("  ").Error.Code.ShouldBe("PurchaseOrder.ClosureReasonRequired");

        order.Close("Enough").IsSuccess.ShouldBeTrue();
        order.Close("Again").Error.Code.ShouldBe("PurchaseOrder.AlreadyFinished");
    }

    [Fact]
    public void A_charge_mapped_to_a_purchase_rather_than_an_order_is_refused()
    {
        PurchaseOrder order = Draft().Value;

        order.AddCharge(Charge(ChargeableDocument.Purchase), 30m)
            .Error.Code.ShouldBe("PurchaseOrder.ChargeNotForOrders");
    }

    [Fact]
    public void The_expected_total_is_the_goods_the_tax_and_the_charges()
    {
        PurchaseOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        order.AddLine(product, unit, 4m, 4m, 25m, Assessed(100m, 5m)).IsSuccess.ShouldBeTrue();
        order.AddCharge(Charge(ChargeableDocument.PurchaseOrder), 20m).IsSuccess.ShouldBeTrue();

        order.Taxable.Amount.ShouldBe(100m);
        order.Tax.Amount.ShouldBe(5m);
        order.ChargeTotal.Amount.ShouldBe(20m);
        order.Total.Amount.ShouldBe(125m);
    }

    [Fact]
    public void A_return_cannot_be_raised_from_an_order()
    {
        // A return that filled an order would take its outstanding quantity down as though
        // the supplier had delivered, which is the opposite of what happened.
        PurchaseInvoice.CreateDraft(
            Tenant,
            FirmKey,
            Branch,
            Year,
            "PR/2026/0001",
            Date,
            PartyNamed(LedgerKind.Supplier),
            Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
            TaxMode.Tax,
            Qar,
            PurchaseDocumentKind.Return,
            returnsInvoiceId: null,
            purchaseOrderId: PurchaseOrderId.NewId())
            .Error.Code.ShouldBe("PurchaseInvoice.ReturnFromOrder");
    }

    // ------------------------------------------------------------------ scaffolding

    private static Result<PurchaseOrder> Draft(
        Ledger? party = null,
        DateOnly? expectedOn = null) =>
        PurchaseOrder.CreateDraft(
            Tenant,
            FirmKey,
            Branch,
            Year,
            "PO/2026/0001",
            Date,
            party ?? PartyNamed(LedgerKind.Supplier),
            Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
            TaxMode.Tax,
            Qar,
            expectedOn);

    /// <summary>An order for one line, confirmed and waiting on a supplier.</summary>
    private static PurchaseOrder Confirmed(decimal quantity, decimal rate)
    {
        PurchaseOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        order.AddLine(
            product, unit, quantity, quantity, rate, Assessed(quantity * rate, 0m))
            .IsSuccess.ShouldBeTrue();

        order.Confirm(User, Now).IsSuccess.ShouldBeTrue();

        return order;
    }

    private static TaxAssessment Assessed(decimal taxable, decimal tax) =>
        TaxCalculator.Calculate(
            Money.Of(taxable, Qar),
            TaxRate.FromTrusted(
                taxable == 0m ? 0m : decimal.Round(tax / taxable * 100m, 6)),
            new TaxContext(
                TaxRegime.GccVat, DocumentTaxMode.Taxable, false, false));

    private static (Product Product, UnitOfMeasure Unit) Made()
    {
        UnitOfMeasure unit = UnitOfMeasure.CreateBase(Tenant, FirmKey, "EACH", "Each").Value;

        Product product = Product.Create(
            Category.CreateRoot(Tenant, FirmKey, "GEN", "General").Value,
            unit,
            $"PRO-{Guid.NewGuid():N}"[..12],
            "A thing",
            ItemType.Stock,
            Qar).Value;

        return (product, unit);
    }

    private static AdditionalLedger Charge(ChargeableDocument document) =>
        AdditionalLedger.Map(
            Tenant,
            FirmKey,
            document,
            LedgerNamed("CARRIAGE", LedgerKind.AdditionalCharge),
            isAddition: true).Value;

    private static Ledger PartyNamed(LedgerKind kind) =>
        LedgerNamed(kind == LedgerKind.Supplier ? "2200" : "1200", kind);

    private static Ledger LedgerNamed(string code, LedgerKind kind) =>
        Ledger.Create(
            AccountGroup.CreateRoot(
                Tenant, FirmKey, $"G{code}", $"Group {code}", AccountNature.Asset).Value,
            code,
            code,
            kind,
            Qar).Value;
}
