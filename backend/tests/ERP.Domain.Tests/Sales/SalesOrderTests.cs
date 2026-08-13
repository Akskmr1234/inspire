using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Sales;

/// <summary>Tests for <see cref="SalesOrder"/>: what a customer asked for, and what is left.</summary>
/// <remarks>
/// Most of the shape is an invoice's and is tested there. What is worth reading here is
/// the one column an order adds - how much of each line has gone out - and the four states
/// it drives: a confirmed order fills, completes itself, reopens when an invoice is
/// cancelled, and refuses to be invoiced past what was ordered.
/// </remarks>
public sealed class SalesOrderTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId FirmKey = FirmId.NewId();
    private static readonly BranchId Branch = BranchId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateOnly Date = new(2026, 8, 13);
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly FinancialYear Year = FinancialYear.Create(
        Tenant, FirmKey, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [])
        .Value;

    [Fact]
    public void An_order_taken_from_somebody_who_is_not_a_customer_is_refused()
    {
        Draft(party: PartyNamed(LedgerKind.Supplier))
            .Error.Code.ShouldBe("SalesOrder.NotACustomer");
    }

    [Fact]
    public void A_delivery_promised_before_the_order_was_taken_is_refused()
    {
        // The mistake that would otherwise show up as an order overdue on the day it was
        // entered.
        Draft(expectedOn: Date.AddDays(-1))
            .Error.Code.ShouldBe("SalesOrder.ExpectedBeforeOrdered");
    }

    [Fact]
    public void An_order_with_nothing_on_it_cannot_be_confirmed()
    {
        SalesOrder order = Draft().Value;

        order.Confirm(User, Now).Error.Code.ShouldBe("SalesOrder.NoLines");
    }

    [Fact]
    public void A_confirmed_order_can_no_longer_be_changed()
    {
        SalesOrder order = Confirmed(10m, 50m);
        (Product product, UnitOfMeasure unit) = Made();

        order.AddLine(product, unit, 1m, 1m, 50m, Assessed(50m, 0m))
            .Error.Code.ShouldBe("SalesOrder.NotEditable");
    }

    [Fact]
    public void Nothing_can_be_invoiced_against_a_draft()
    {
        SalesOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        SalesOrderLine line = order
            .AddLine(product, unit, 10m, 10m, 50m, Assessed(500m, 0m)).Value;

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 1m })
            .Error.Code.ShouldBe("SalesOrder.NotOpen");
    }

    [Fact]
    public void Part_of_a_line_goes_out_and_the_rest_stays_owed()
    {
        SalesOrder order = Confirmed(10m, 50m);
        SalesOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        line.InvoicedQuantity.ShouldBe(4m);
        line.OutstandingQuantity.ShouldBe(6m);
        line.IsFulfilled.ShouldBeFalse();

        // Still open, because the customer is still owed six.
        order.Status.ShouldBe(SalesOrderStatus.Confirmed);
        order.IsPartlyInvoiced.ShouldBeTrue();
    }

    [Fact]
    public void An_order_completes_itself_when_the_last_line_is_filled()
    {
        // Nobody has to remember to close it, which is the difference between an
        // outstanding-orders report worth reading and one full of finished work.
        SalesOrder order = Confirmed(10m, 50m);
        SalesOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 6m })
            .IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(SalesOrderStatus.Confirmed);

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Completed);
        order.Lines[0].OutstandingQuantity.ShouldBe(0m);
    }

    [Fact]
    public void A_line_cannot_be_invoiced_for_more_than_was_ordered()
    {
        SalesOrder order = Confirmed(10m, 50m);
        SalesOrderLine line = order.Lines[0];

        Result over = order.RecordInvoiced(
            new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 11m });

        over.Error.Code.ShouldBe("SalesOrder.OverInvoiced");

        // And nothing was recorded, because the check runs before any of it is applied.
        line.InvoicedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void One_bad_line_leaves_every_other_line_untouched()
    {
        // The reason the quantities are checked in full before any are applied: an order
        // claiming goods went out that no invoice carries is worse than a refusal.
        SalesOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        SalesOrderLine first = order
            .AddLine(product, unit, 5m, 5m, 10m, Assessed(50m, 0m)).Value;
        SalesOrderLine second = order
            .AddLine(product, unit, 5m, 5m, 10m, Assessed(50m, 0m)).Value;

        order.Confirm(User, Now).IsSuccess.ShouldBeTrue();

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal>
        {
            [first.Id] = 2m,
            [second.Id] = 99m,
        }).Error.Code.ShouldBe("SalesOrder.OverInvoiced");

        first.InvoicedQuantity.ShouldBe(0m);
        second.InvoicedQuantity.ShouldBe(0m);
    }

    [Fact]
    public void A_completed_order_reopens_when_an_invoice_is_cancelled()
    {
        SalesOrder order = Confirmed(10m, 50m);
        SalesOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 10m })
            .IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(SalesOrderStatus.Completed);

        order.ReleaseInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();

        order.Status.ShouldBe(SalesOrderStatus.Confirmed);
        line.OutstandingQuantity.ShouldBe(4m);
    }

    [Fact]
    public void An_order_somebody_closed_deliberately_does_not_reopen()
    {
        // Reopening it would put work back in front of a warehouse that was told to stop.
        SalesOrder order = Confirmed(10m, 50m);
        SalesOrderLine line = order.Lines[0];

        order.RecordInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 4m })
            .IsSuccess.ShouldBeTrue();
        order.Close("The customer cancelled the rest").IsSuccess.ShouldBeTrue();

        order.ReleaseInvoiced(new Dictionary<SalesOrderLineId, decimal> { [line.Id] = 4m })
            .Error.Code.ShouldBe("SalesOrder.NotReopenable");
    }

    [Fact]
    public void A_closed_order_takes_no_more_invoices()
    {
        SalesOrder order = Confirmed(10m, 50m);

        order.Close("Customer went elsewhere").IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(SalesOrderStatus.Cancelled);
        order.ClosureReason.ShouldBe("Customer went elsewhere");

        order.RecordInvoiced(
            new Dictionary<SalesOrderLineId, decimal> { [order.Lines[0].Id] = 1m })
            .Error.Code.ShouldBe("SalesOrder.NotOpen");
    }

    [Fact]
    public void Closing_needs_a_reason_and_happens_once()
    {
        SalesOrder order = Confirmed(10m, 50m);

        order.Close("  ").Error.Code.ShouldBe("SalesOrder.ClosureReasonRequired");

        order.Close("Enough").IsSuccess.ShouldBeTrue();
        order.Close("Again").Error.Code.ShouldBe("SalesOrder.AlreadyFinished");
    }

    [Fact]
    public void A_charge_mapped_to_an_invoice_rather_than_an_order_is_refused()
    {
        SalesOrder order = Draft().Value;

        order.AddCharge(Charge(ChargeableDocument.Sales), 30m)
            .Error.Code.ShouldBe("SalesOrder.ChargeNotForOrders");
    }

    [Fact]
    public void The_quoted_total_is_the_goods_the_tax_and_the_charges()
    {
        SalesOrder order = Draft().Value;
        (Product product, UnitOfMeasure unit) = Made();

        order.AddLine(product, unit, 4m, 4m, 25m, Assessed(100m, 5m)).IsSuccess.ShouldBeTrue();
        order.AddCharge(Charge(ChargeableDocument.SalesOrder), 20m).IsSuccess.ShouldBeTrue();

        order.Taxable.Amount.ShouldBe(100m);
        order.Tax.Amount.ShouldBe(5m);
        order.ChargeTotal.Amount.ShouldBe(20m);
        order.Total.Amount.ShouldBe(125m);
    }

    // ------------------------------------------------------------------ scaffolding

    private static Result<SalesOrder> Draft(
        Ledger? party = null,
        DateOnly? expectedOn = null) =>
        SalesOrder.CreateDraft(
            Tenant,
            FirmKey,
            Branch,
            Year,
            "SO/2026/0001",
            Date,
            party ?? PartyNamed(LedgerKind.Customer),
            Warehouse.Create(Tenant, FirmKey, "MAIN", "Main store").Value,
            TaxMode.Tax,
            Qar,
            expectedOn);

    /// <summary>An order for one line, confirmed and ready to fill.</summary>
    private static SalesOrder Confirmed(decimal quantity, decimal rate)
    {
        SalesOrder order = Draft().Value;
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
            LedgerNamed("FREIGHT", LedgerKind.AdditionalCharge),
            isAddition: true).Value;

    private static Ledger PartyNamed(LedgerKind kind) =>
        LedgerNamed(kind == LedgerKind.Customer ? "1200" : "2200", kind);

    private static Ledger LedgerNamed(string code, LedgerKind kind) =>
        Ledger.Create(
            AccountGroup.CreateRoot(
                Tenant, FirmKey, $"G{code}", $"Group {code}", AccountNature.Asset).Value,
            code,
            code,
            kind,
            Qar).Value;
}
