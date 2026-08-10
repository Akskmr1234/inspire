using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>Tests for <see cref="SerialNumber"/>: one row per physical thing.</summary>
/// <remarks>
/// Section 12.7. What these cover is the promise the section actually makes - a unit
/// that has gone out never comes back round on its own - and the states either side of
/// it, because every one of them is quoted back at a service desk that has the machine
/// in front of it.
/// </remarks>
public sealed class SerialNumberTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly WarehouseId Main = WarehouseId.NewId();
    private static readonly WarehouseId Shop = WarehouseId.NewId();
    private static readonly StockDocumentId Receipt = StockDocumentId.NewId();
    private static readonly DateOnly Today = new(2026, 8, 10);

    [Fact]
    public void A_unit_is_written_down_before_it_is_in_stock()
    {
        // The document naming it may still be a draft, and a draft moves nothing.
        // Offering a draft's units for sale would be a draft receipt raising a
        // position by another name.
        SerialNumber unit = Received();

        unit.Status.ShouldBe(SerialStatus.Recorded);
        unit.IsAvailable.ShouldBeFalse();

        unit.TakeIntoStock(Main, Today, Receipt).IsSuccess.ShouldBeTrue();

        unit.Status.ShouldBe(SerialStatus.InStock);
        unit.IsAvailable.ShouldBeTrue();
        unit.WarehouseId.ShouldBe(Main);
    }

    [Fact]
    public void A_unit_of_a_product_that_is_not_serialised_is_refused()
    {
        SerialNumber.Receive(
                Tenant, Firm, Stocked(), "IMEI-1", Main, Today, Receipt)
            .Error.Code.ShouldBe("Serial.NotTracked");
    }

    [Fact]
    public void A_number_is_required_and_kept_in_one_case()
    {
        Product product = Serialised();

        SerialNumber.Receive(Tenant, Firm, product, "  ", Main, Today, Receipt)
            .Error.Code.ShouldBe("Serial.NumberRequired");

        SerialNumber.Receive(
                Tenant, Firm, product, new string('x', 61), Main, Today, Receipt)
            .Error.Code.ShouldBe("Serial.NumberTooLong");

        SerialNumber.Receive(Tenant, Firm, product, " imei-7a ", Main, Today, Receipt)
            .Value.Number.ShouldBe("IMEI-7A");
    }

    [Fact]
    public void A_unit_that_has_gone_out_never_goes_out_again()
    {
        // The promise section 12.7 makes about sold serials, and the only way to keep
        // it is to refuse the second issue rather than hope nobody attempts one.
        SerialNumber unit = InStock();

        unit.Issue(Today, StockDocumentId.NewId()).IsSuccess.ShouldBeTrue();
        unit.Status.ShouldBe(SerialStatus.Issued);
        unit.WarehouseId.ShouldBeNull();

        unit.Issue(Today, StockDocumentId.NewId()).Error.Code.ShouldBe("Serial.NotAvailable");
    }

    [Fact]
    public void A_unit_back_from_a_customer_is_on_the_shelf_again()
    {
        SerialNumber unit = InStock();
        unit.Issue(Today, StockDocumentId.NewId());

        unit.ReturnFromCustomer(Shop, Today.AddDays(7), StockDocumentId.NewId())
            .IsSuccess.ShouldBeTrue();

        unit.Status.ShouldBe(SerialStatus.ReturnedFromCustomer);
        unit.IsAvailable.ShouldBeTrue();
        unit.WarehouseId.ShouldBe(Shop);

        // And it can go out again, which is what "available" has to mean.
        unit.Issue(Today.AddDays(8), StockDocumentId.NewId()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_unit_still_on_the_shelf_cannot_come_back_from_a_customer()
    {
        // Somebody has the wrong number in front of them, and accepting it would
        // quietly overwrite where the unit actually is.
        InStock().ReturnFromCustomer(Shop, Today, StockDocumentId.NewId())
            .Error.Code.ShouldBe("Serial.NotIssued");
    }

    [Fact]
    public void A_unit_sent_back_to_its_supplier_is_gone_for_good()
    {
        // If the supplier replaces it, what arrives is a different machine with a
        // different number: recording it as this one returning would put a warranty
        // and a service history on the wrong unit.
        SerialNumber unit = InStock();

        unit.ReturnToSupplier(Today, StockDocumentId.NewId()).IsSuccess.ShouldBeTrue();

        unit.Status.ShouldBe(SerialStatus.ReturnedToSupplier);
        unit.IsAvailable.ShouldBeFalse();
        unit.ReturnFromCustomer(Main, Today, StockDocumentId.NewId())
            .Error.Code.ShouldBe("Serial.NotIssued");
    }

    [Fact]
    public void A_transfer_moves_the_unit_without_changing_what_it_is()
    {
        SerialNumber unit = InStock();

        unit.TransferTo(Shop, StockDocumentId.NewId()).IsSuccess.ShouldBeTrue();

        unit.WarehouseId.ShouldBe(Shop);
        unit.Status.ShouldBe(SerialStatus.InStock);

        unit.TransferTo(Shop, StockDocumentId.NewId()).Error.Code
            .ShouldBe("Serial.SameWarehouse");
    }

    [Fact]
    public void Cancelling_the_receipt_that_wrote_a_unit_down_unwrites_it()
    {
        SerialNumber unit = InStock();

        unit.UndoReceipt(Receipt).IsSuccess.ShouldBeTrue();

        unit.Status.ShouldBe(SerialStatus.Recorded);
        unit.WarehouseId.ShouldBeNull();
        unit.ReceivedOn.ShouldBeNull();
    }

    [Fact]
    public void Only_the_document_that_brought_a_unit_in_can_un_bring_it()
    {
        SerialNumber unit = InStock();

        unit.UndoReceipt(StockDocumentId.NewId()).Error.Code.ShouldBe("Serial.NotItsOrigin");

        // And not once the unit has moved on: it is with somebody else, and the honest
        // record of that is an adjustment.
        unit.Issue(Today, StockDocumentId.NewId());
        unit.UndoReceipt(Receipt).Error.Code.ShouldBe("Serial.NotAvailable");
    }

    [Fact]
    public void Cancelling_an_issue_puts_the_unit_back_on_its_shelf()
    {
        SerialNumber unit = InStock();
        StockDocumentId issue = StockDocumentId.NewId();

        unit.Issue(Today, issue);
        unit.UndoIssue(Main, issue).IsSuccess.ShouldBeTrue();

        unit.Status.ShouldBe(SerialStatus.InStock);
        unit.WarehouseId.ShouldBe(Main);
        unit.IssuedOn.ShouldBeNull();
    }

    [Fact]
    public void Warranty_runs_to_a_date_and_covers_that_day()
    {
        SerialNumber unit = InStock();

        unit.IsUnderWarrantyOn(Today).ShouldBeFalse();

        unit.SetWarranty(Today.AddYears(1)).IsSuccess.ShouldBeTrue();

        unit.IsUnderWarrantyOn(Today.AddYears(1)).ShouldBeTrue();
        unit.IsUnderWarrantyOn(Today.AddYears(1).AddDays(1)).ShouldBeFalse();

        unit.SetWarranty(Today.AddDays(-1)).Error.Code
            .ShouldBe("Serial.WarrantyBeforeReceipt");
    }

    // ------------------------------------------------------------------ helpers

    private static SerialNumber Received() => SerialNumber.Receive(
        Tenant, Firm, Serialised(), $"IMEI-{Guid.NewGuid():N}"[..12], Main, Today, Receipt,
        unitCost: 900m).Value;

    private static SerialNumber InStock()
    {
        SerialNumber unit = Received();

        unit.TakeIntoStock(Main, Today, Receipt).IsSuccess.ShouldBeTrue();

        return unit;
    }

    private static Product Serialised()
    {
        Product product = Stocked();

        product.SetTracking(false, true);

        return product;
    }

    private static Product Stocked()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

        return Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            each, $"PRO-{Guid.NewGuid():N}"[..12], "A handset", ItemType.Stock,
            CurrencyCode.Qar).Value;
    }
}
