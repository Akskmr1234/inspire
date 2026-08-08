using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>Tests for <see cref="StockDocument"/>.</summary>
/// <remarks>
/// The document owns the rules about what may be entered; the balances own what
/// happens when it posts. These cover the first half - the shape of a transfer, the
/// direction a quantity is allowed to point, and which documents carry a cost -
/// because those are the ones a screen can get wrong in a way nothing downstream
/// would notice.
/// </remarks>
public sealed class StockDocumentTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly UserId User = UserId.NewId();
    private static readonly DateOnly Date = new(2026, 8, 7);
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_receipt_is_drafted_against_one_warehouse()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);

        document.Status.ShouldBe(StockDocumentStatus.Draft);
        document.IsEditable.ShouldBeTrue();
        document.DestinationWarehouseId.ShouldBeNull();
        document.CarriesRate.ShouldBeTrue();
        document.AllowsSignedQuantity.ShouldBeFalse();
    }

    [Fact]
    public void A_transfer_needs_a_destination_and_two_different_warehouses()
    {
        Warehouse main = Godown("MAIN");
        Warehouse shop = Godown("SHOP");

        TryDraft(StockDocumentType.StockTransfer, main).Error.Code
            .ShouldBe("StockDocument.DestinationRequired");

        TryDraft(StockDocumentType.StockTransfer, main, main).Error.Code
            .ShouldBe("StockDocument.SameWarehouse");

        StockDocument transfer =
            TryDraft(StockDocumentType.StockTransfer, main, shop).Value;

        transfer.DestinationWarehouseId.ShouldBe(shop.Id);
        transfer.IsTransfer.ShouldBeTrue();
    }

    [Fact]
    public void A_destination_on_anything_but_a_transfer_is_refused()
    {
        // Silently ignoring it would leave somebody believing an issue had moved
        // goods into the warehouse they named.
        TryDraft(StockDocumentType.MaterialIssue, Godown("MAIN"), Godown("SHOP"))
            .Error.Code.ShouldBe("StockDocument.DestinationNotAllowed");
    }

    [Fact]
    public void A_withdrawn_warehouse_cannot_be_used()
    {
        Warehouse closed = Godown("OLD");
        closed.Deactivate();

        TryDraft(StockDocumentType.MaterialReceipt, closed).Error.Code
            .ShouldBe("StockDocument.WarehouseWithdrawn");
    }

    [Fact]
    public void A_document_outside_the_financial_year_is_refused()
    {
        Result<StockDocument> result = StockDocument.CreateDraft(
            Tenant, Firm, Year(), StockDocumentType.MaterialReceipt, "MR-0001",
            new DateOnly(2030, 1, 1), Godown("MAIN"));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_line_records_the_quantity_as_entered_and_as_stocked()
    {
        // Four cases of twenty-four is ninety-six pieces, and both figures are facts:
        // the note has to print what was received, the balance has to hold what is
        // stocked.
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();
        UnitOfMeasure box = Derived(each, "BOX", 24m);

        StockDocumentLine line = document
            .AddLine(Stocked(each), box, 4m, 96m, rate: 5m).Value;

        line.Quantity.ShouldBe(4m);
        line.StockQuantity.ShouldBe(96m);
        line.UnitId.ShouldBe(box.Id);
        line.Rate.ShouldBe(5m);
        line.LineNumber.ShouldBe(1);
    }

    [Fact]
    public void A_service_or_non_stock_item_cannot_move()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();

        document.AddLine(Stocked(each, ItemType.Service), each, 1m, 1m, 10m)
            .Error.Code.ShouldBe("StockDocument.NotStocked");

        document.AddLine(Stocked(each, ItemType.NonStock), each, 1m, 1m, 10m)
            .Error.Code.ShouldBe("StockDocument.NotStocked");
    }

    [Fact]
    public void A_product_from_another_firm_cannot_move()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);

        // The whole product belongs to the other firm, masters included: the product
        // aggregate already refuses to be assembled from two firms' masters, so a
        // half-foreign one could not exist to be tested with.
        FirmId theirFirm = FirmId.NewId();
        UnitOfMeasure theirUnit =
            UnitOfMeasure.CreateBase(Tenant, theirFirm, "EACH", "Each").Value;

        Product theirs = Product.Create(
            Category.CreateRoot(Tenant, theirFirm, "C", "Theirs").Value,
            theirUnit, "P-1", "Somebody else's", ItemType.Stock, CurrencyCode.Qar).Value;

        document.AddLine(theirs, theirUnit, 1m, 1m, 10m).Error.Code
            .ShouldBe("StockDocument.ProductNotInFirm");
    }

    [Fact]
    public void Only_an_adjustment_may_carry_a_negative_quantity()
    {
        // Found stock and lost stock are one correction pointed two ways. Every other
        // document carries its direction in its type, so a negative there would mean
        // two things at once.
        UnitOfMeasure each = BaseUnit();

        Draft(StockDocumentType.MaterialIssue).AddLine(Stocked(each), each, -1m, -1m)
            .Error.Code.ShouldBe("StockDocument.QuantityNegative");

        StockDocument adjustment = Draft(StockDocumentType.StockAdjustment);
        adjustment.AddLine(Stocked(each), each, -3m, -3m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_line_for_no_quantity_is_refused()
    {
        UnitOfMeasure each = BaseUnit();

        Draft(StockDocumentType.MaterialReceipt).AddLine(Stocked(each), each, 0m, 0m, 5m)
            .Error.Code.ShouldBe("StockDocument.QuantityZero");
    }

    [Fact]
    public void Only_the_documents_that_bring_goods_in_carry_a_rate()
    {
        // An issue is valued at what the position already says the goods cost. A rate
        // there would be recorded, displayed, and ignored - which is worse than being
        // refused, because somebody would set it and believe it had done something.
        UnitOfMeasure each = BaseUnit();

        Draft(StockDocumentType.MaterialIssue).AddLine(Stocked(each), each, 1m, 1m, 9m)
            .Error.Code.ShouldBe("StockDocument.RateNotAllowed");

        Draft(StockDocumentType.StockTransfer, transfer: true)
            .AddLine(Stocked(each), each, 1m, 1m, 9m)
            .Error.Code.ShouldBe("StockDocument.RateNotAllowed");

        Draft(StockDocumentType.OpeningStock).AddLine(Stocked(each), each, 1m, 1m, 9m)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_quantity_too_precise_for_its_unit_is_refused()
    {
        UnitOfMeasure each = BaseUnit();

        Draft(StockDocumentType.MaterialReceipt)
            .AddLine(Stocked(each), each, 1.5m, 1.5m, 5m)
            .Error.Code.ShouldBe("UnitOfMeasure.TooPrecise");
    }

    [Fact]
    public void Removing_a_line_renumbers_the_rest()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();

        StockDocumentLine first = document.AddLine(Stocked(each), each, 1m, 1m, 5m).Value;
        document.AddLine(Stocked(each), each, 2m, 2m, 5m);
        document.AddLine(Stocked(each), each, 3m, 3m, 5m);

        document.RemoveLine(first.Id).IsSuccess.ShouldBeTrue();

        document.Lines.Count.ShouldBe(2);
        document.Lines.Select(line => line.LineNumber).ShouldBe([1, 2]);
    }

    [Fact]
    public void An_empty_document_cannot_be_posted()
    {
        Draft(StockDocumentType.MaterialReceipt).Post(User, Now)
            .Error.Code.ShouldBe("StockDocument.NoLines");
    }

    [Fact]
    public void One_product_twice_on_one_document_is_refused()
    {
        // The second line would be valued at an average the first had just moved, so
        // the document's own two lines would disagree about what the goods cost.
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();
        Product product = Stocked(each);

        document.AddLine(product, each, 1m, 1m, 5m);
        document.AddLine(product, each, 2m, 2m, 7m);

        document.Post(User, Now).Error.Code.ShouldBe("StockDocument.DuplicateProduct");
    }

    [Fact]
    public void A_batched_product_must_say_which_batch_moved()
    {
        // Without it the line would move the product's position and no batch position,
        // so the two would stop adding up from that document onwards.
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();
        Product product = Batched(each);

        document.AddLine(product, each, 1m, 1m, 5m).Error.Code
            .ShouldBe("StockDocument.BatchRequired");

        Batch batch = Batch.Open(Tenant, Firm, product, "A001").Value;

        document.AddLine(product, each, 1m, 1m, 5m, batch).Value.BatchId
            .ShouldBe(batch.Id);
    }

    [Fact]
    public void A_batch_on_a_product_that_is_not_batched_is_refused()
    {
        // Recorded, printed, and ignored by the position is worse than refused: the
        // number would mean nothing to the stock it appears to describe.
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();
        Product batched = Batched(each);
        Batch batch = Batch.Open(Tenant, Firm, batched, "A001").Value;

        document.AddLine(Stocked(each), each, 1m, 1m, 5m, batch).Error.Code
            .ShouldBe("StockDocument.BatchNotTracked");
    }

    [Fact]
    public void A_batch_of_another_product_cannot_move()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();
        Product theirs = Batched(each);
        Batch batch = Batch.Open(Tenant, Firm, theirs, "A001").Value;

        document.AddLine(Batched(each), each, 1m, 1m, 5m, batch).Error.Code
            .ShouldBe("StockDocument.BatchWrongProduct");
    }

    [Fact]
    public void One_product_may_appear_twice_in_two_batches()
    {
        // The exception to the duplicate rule, and the case tracking batches exists
        // for: an issue of thirty from a lot holding twenty draws the rest from
        // another, and the two leave at two costs carrying two expiry dates.
        StockDocument document = Draft(StockDocumentType.MaterialIssue);
        UnitOfMeasure each = BaseUnit();
        Product product = Batched(each);

        Batch first = Batch.Open(Tenant, Firm, product, "A001").Value;
        Batch second = Batch.Open(Tenant, Firm, product, "A002").Value;

        document.AddLine(product, each, 20m, 20m, batch: first).IsSuccess.ShouldBeTrue();
        document.AddLine(product, each, 10m, 10m, batch: second).IsSuccess.ShouldBeTrue();

        document.Post(User, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_same_batch_twice_on_one_document_is_still_refused()
    {
        StockDocument document = Draft(StockDocumentType.MaterialIssue);
        UnitOfMeasure each = BaseUnit();
        Product product = Batched(each);
        Batch batch = Batch.Open(Tenant, Firm, product, "A001").Value;

        document.AddLine(product, each, 20m, 20m, batch: batch);
        document.AddLine(product, each, 10m, 10m, batch: batch);

        document.Post(User, Now).Error.Code.ShouldBe("StockDocument.DuplicateProduct");
    }

    [Fact]
    public void Only_the_documents_that_bring_goods_in_may_open_a_batch()
    {
        // A transfer or an issue moves goods that are already somewhere, so a batch
        // number it does not recognise is a typing mistake rather than a new lot.
        Draft(StockDocumentType.MaterialReceipt).OpensBatches.ShouldBeTrue();
        Draft(StockDocumentType.StockAdjustment).OpensBatches.ShouldBeTrue();
        Draft(StockDocumentType.PhysicalVerification).OpensBatches.ShouldBeTrue();
        Draft(StockDocumentType.MaterialIssue).OpensBatches.ShouldBeFalse();
        Draft(StockDocumentType.StockTransfer, transfer: true).OpensBatches.ShouldBeFalse();

        // Counting a shelf means reading a number off a carton. Generating one would
        // file the count against a lot that exists nowhere but here.
        Draft(StockDocumentType.PhysicalVerification).GeneratesBatchNumbers.ShouldBeFalse();
        Draft(StockDocumentType.MaterialReceipt).GeneratesBatchNumbers.ShouldBeTrue();
    }

    [Fact]
    public void A_posted_document_is_closed_to_further_change()
    {
        StockDocument document = Posted();
        UnitOfMeasure each = BaseUnit();

        document.Status.ShouldBe(StockDocumentStatus.Posted);
        document.PostedAtUtc.ShouldBe(Now);
        document.PostedBy.ShouldBe(User);
        document.IsEditable.ShouldBeFalse();

        document.AddLine(Stocked(each), each, 1m, 1m, 5m).Error.Code
            .ShouldBe("StockDocument.NotEditable");
        document.RemoveLine(document.Lines[0].Id).Error.Code
            .ShouldBe("StockDocument.NotEditable");
        document.SetDetails("REF", "note").Error.Code
            .ShouldBe("StockDocument.NotEditable");
        document.Post(User, Now).Error.Code.ShouldBe("StockDocument.AlreadyPosted");
    }

    [Fact]
    public void Only_a_posted_document_can_be_cancelled_and_only_with_a_reason()
    {
        Draft(StockDocumentType.MaterialReceipt).Cancel("wrong godown")
            .Error.Code.ShouldBe("StockDocument.NotPosted");

        StockDocument posted = Posted();
        posted.Cancel("  ").Error.Code
            .ShouldBe("StockDocument.CancellationReasonRequired");

        posted.Cancel("Entered against the wrong godown").IsSuccess.ShouldBeTrue();
        posted.Status.ShouldBe(StockDocumentStatus.Cancelled);
        posted.Number.ShouldNotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------ helpers

    private static FinancialYear Year() =>
        FinancialYear.Create(
            Tenant, Firm, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            []).Value;

    private static Warehouse Godown(string code) =>
        Warehouse.Create(Tenant, Firm, code, $"{code} godown").Value;

    private static UnitOfMeasure BaseUnit() =>
        UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

    private static UnitOfMeasure Derived(UnitOfMeasure baseUnit, string code, decimal factor) =>
        UnitOfMeasure.CreateDerived(baseUnit, code, code, factor).Value;

    private static Product Stocked(UnitOfMeasure unit, ItemType itemType = ItemType.Stock) =>
        Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            unit, $"PRO-{Guid.NewGuid():N}"[..12], "A thing", itemType,
            CurrencyCode.Qar).Value;

    private static Product Batched(UnitOfMeasure unit)
    {
        Product product = Stocked(unit);

        product.SetTracking(true, false);

        return product;
    }

    private static Result<StockDocument> TryDraft(
        StockDocumentType type,
        Warehouse warehouse,
        Warehouse? destination = null) =>
        StockDocument.CreateDraft(
            Tenant, Firm, Year(), type, "SD-0001", Date, warehouse, destination);

    private static StockDocument Draft(StockDocumentType type, bool transfer = false) =>
        TryDraft(type, Godown("MAIN"), transfer ? Godown("SHOP") : null).Value;

    private static StockDocument Posted()
    {
        StockDocument document = Draft(StockDocumentType.MaterialReceipt);
        UnitOfMeasure each = BaseUnit();

        document.AddLine(Stocked(each), each, 10m, 10m, 5m);
        document.Post(User, Now);

        return document;
    }
}
