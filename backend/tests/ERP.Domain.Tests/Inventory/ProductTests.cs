using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>
/// Tests for <see cref="Product"/>.
/// </summary>
/// <remarks>
/// The master every other module reaches for, and the one where a wrong rule is most
/// expensive: a product filed under a sibling firm's category lands on the wrong
/// company's stock report, and a purchase unit that does not convert to the stock unit
/// produces a quantity that means nothing. Those are the rules pinned here, along with
/// the ones that keep a service from claiming to have serial numbers.
/// </remarks>
public sealed class ProductTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;

    // ------------------------------------------------------------------ creating

    [Fact]
    public void A_new_product_is_active_and_costs_nothing_yet()
    {
        // A product may legitimately be set up before anybody knows what it costs -
        // that is what the first purchase is for.
        Product product = Create();

        product.IsActive.ShouldBeTrue();
        product.IsDiscontinued.ShouldBeFalse();
        product.IsTransactable.ShouldBeTrue();
        product.Cost.ShouldBe(Money.Of(0m, Qar));
        product.Rates.ShouldBe(ProductRates.Empty);
        product.Barcodes.ShouldBeEmpty();
    }

    [Fact]
    public void The_code_is_folded_to_upper_case_and_the_description_trimmed()
    {
        Product product = Create(code: "pro-1004", description: "  Galaxy A54  ");

        product.Code.ShouldBe("PRO-1004");
        product.Description.ShouldBe("Galaxy A54");
    }

    [Fact]
    public void A_product_starts_bought_and_sold_in_the_unit_it_is_stocked_in()
    {
        // The safe default. Anything else would have to be guessed, and a wrong guess
        // silently multiplies every purchase quantity.
        UnitOfMeasure each = BaseUnit();
        Product product = Create(stockUnit: each);

        product.StockUnitId.ShouldBe(each.Id);
        product.PurchaseUnitId.ShouldBe(each.Id);
        product.SalesUnitId.ShouldBe(each.Id);
    }

    [Fact]
    public void A_product_cannot_be_filed_under_another_firms_category()
    {
        // No tenant filter catches this: the firms share a tenant, and the category
        // reads perfectly well. The product would simply appear on the wrong
        // company's stock report.
        Category theirs = Category.CreateRoot(Tenant, FirmId.NewId(), "PHONES", "Phones").Value;
        UnitOfMeasure ours = BaseUnit();

        Product.Create(theirs, ours, "PRO-1", "Galaxy A54", ItemType.Stock, Qar)
            .Error.Code.ShouldBe("Product.UnitFromAnotherFirm");
    }

    [Fact]
    public void A_deactivated_category_takes_no_new_products()
    {
        Category retired = RootCategory();
        retired.Deactivate();

        Product.Create(retired, BaseUnit(), "PRO-1", "Galaxy A54", ItemType.Stock, Qar)
            .Error.Code.ShouldBe("Product.CategoryInactive");
    }

    [Theory]
    [InlineData("", "Galaxy A54", "Product.CodeRequired")]
    [InlineData("   ", "Galaxy A54", "Product.CodeRequired")]
    [InlineData("PRO-1", "", "Product.DescriptionRequired")]
    [InlineData("PRO-1", "  ", "Product.DescriptionRequired")]
    public void Identity_is_required(string code, string description, string expected)
    {
        TryCreate(code, description).Error.Code.ShouldBe(expected);
    }

    [Fact]
    public void A_code_and_a_description_are_bounded()
    {
        TryCreate(new string('X', Product.MaximumCodeLength + 1), "Fine")
            .Error.Code.ShouldBe("Product.CodeTooLong");

        TryCreate("PRO-1", new string('X', Product.MaximumDescriptionLength + 1))
            .Error.Code.ShouldBe("Product.DescriptionTooLong");
    }

    // ------------------------------------------------------------------ units

    [Fact]
    public void Purchase_and_sales_units_may_be_any_unit_that_converts_to_stock()
    {
        UnitOfMeasure each = BaseUnit();
        UnitOfMeasure pack = UnitOfMeasure.CreateDerived(each, "PACK", "Pack of 12", 12m).Value;
        Product product = Create(stockUnit: each);

        product.SetUnits(pack, each, each).IsSuccess.ShouldBeTrue();

        product.PurchaseUnitId.ShouldBe(pack.Id);
        product.SalesUnitId.ShouldBe(each.Id);
    }

    [Fact]
    public void A_unit_from_another_group_is_refused()
    {
        // Buying in kilograms and stocking in litres is not a conversion the system
        // can make. Accepting it would produce a stock quantity that means nothing.
        UnitOfMeasure each = BaseUnit();
        UnitOfMeasure kilogram = UnitOfMeasure.CreateBase(Tenant, Firm, "KG", "Kilogram").Value;
        Product product = Create(stockUnit: each);

        Result result = product.SetUnits(kilogram, each, each);

        result.Error.Code.ShouldBe("Product.UnitNotConvertible");
        product.PurchaseUnitId.ShouldBe(each.Id);
    }

    [Fact]
    public void The_stock_unit_passed_in_must_actually_be_the_products_own()
    {
        // Otherwise the group check above would be made against the wrong unit and
        // would pass for a pair that does not convert.
        UnitOfMeasure each = BaseUnit();
        UnitOfMeasure kilogram = UnitOfMeasure.CreateBase(Tenant, Firm, "KG", "Kilogram").Value;
        Product product = Create(stockUnit: each);

        product.SetUnits(kilogram, kilogram, kilogram)
            .Error.Code.ShouldBe("Product.WrongStockUnit");
    }

    // ------------------------------------------------------------------ rates

    [Fact]
    public void Rates_are_exposed_as_money_in_the_products_own_currency()
    {
        // Held as decimals with one currency on the product, so it cannot disagree
        // with itself about what it is priced in - but nothing outside sees a bare
        // decimal.
        Product product = Create();
        product.SetRates(Rates(cost: 400m, retail: 550m));

        product.Cost.ShouldBe(Money.Of(400m, Qar));
        product.RetailRate.ShouldBe(Money.Of(550m, Qar));
        product.Cost.Currency.ShouldBe(product.Currency);
    }

    [Fact]
    public void A_retail_rate_below_cost_is_allowed()
    {
        // A loss-leader is a decision, not a mistake. Refusing it would be the system
        // overruling a call that is the operator's to make.
        ProductRates.Create(400m, 0m, 0m, 350m, 380m, 360m, 0m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_retail_rate_above_a_stated_mrp_is_refused()
    {
        // Not a preference: the MRP is printed on the pack and selling above it is an
        // offence.
        ProductRates.Create(400m, 0m, 0m, 700m, 500m, 600m, 650m)
            .Error.Code.ShouldBe("Product.RetailAboveMrp");
    }

    [Fact]
    public void An_mrp_of_zero_means_not_applicable_rather_than_a_ceiling_of_nothing()
    {
        // The ordinary case outside Indian retail. Treating zero as a ceiling would
        // forbid every non-zero rate.
        ProductRates.Create(400m, 0m, 0m, 550m, 500m, 520m, 0m).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, 0, -1, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, 0, 0, -1)]
    public void No_rate_may_be_negative(
        decimal cost,
        decimal profit,
        decimal cor,
        decimal retail,
        decimal wholesale,
        decimal other,
        decimal mrp)
    {
        ProductRates.Create(cost, profit, cor, retail, wholesale, other, mrp)
            .Error.Code.ShouldBe("Product.NegativeRate");
    }

    // ------------------------------------------------------------------ stock levels

    [Fact]
    public void Reorder_thresholds_must_ascend()
    {
        StockLevels.Create(10m, 25m, 100m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_minimum_above_the_reorder_level_is_refused()
    {
        // It would read as "already critical, do not order" - the exact opposite of
        // what a reorder level means.
        StockLevels.Create(50m, 25m, 100m).Error.Code.ShouldBe("Product.MinimumAboveReorder");
    }

    [Fact]
    public void A_reorder_level_above_the_maximum_is_refused()
    {
        StockLevels.Create(10m, 150m, 100m).Error.Code.ShouldBe("Product.ReorderAboveMaximum");
    }

    [Fact]
    public void A_maximum_of_zero_means_no_ceiling()
    {
        // Otherwise every product left with a maximum of zero would refuse any
        // reorder level at all.
        StockLevels.Create(10m, 25m, 0m).IsSuccess.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ tracking

    [Fact]
    public void A_stocked_product_can_be_tracked_by_batch_and_by_serial_at_once()
    {
        // A handset arrives in a batch and still has an IMEI of its own. The service
        // module is built on being able to find one.
        Product product = Create();

        product.SetTracking(tracksBatches: true, tracksSerialNumbers: true).IsSuccess
            .ShouldBeTrue();

        product.TracksBatches.ShouldBeTrue();
        product.TracksSerialNumbers.ShouldBeTrue();
    }

    [Fact]
    public void A_service_cannot_be_tracked()
    {
        // There is no physical unit to carry a serial number, and rows in the batch
        // ledger would correspond to nothing.
        Product service = Create(itemType: ItemType.Service);

        service.SetTracking(tracksBatches: false, tracksSerialNumbers: true)
            .Error.Code.ShouldBe("Product.TrackingNeedsStock");
    }

    [Fact]
    public void A_shelf_life_without_batch_tracking_is_refused()
    {
        // Expiry is a property of a batch. Without one there is nothing for the date
        // to attach to, and the expiry report would have nothing to list.
        Product product = Create();

        product.SetTracking(tracksBatches: false, tracksSerialNumbers: false, shelfLifeDays: 180)
            .Error.Code.ShouldBe("Product.ShelfLifeNeedsBatches");
    }

    [Fact]
    public void A_shelf_life_must_be_a_positive_number_of_days()
    {
        Create().SetTracking(tracksBatches: true, tracksSerialNumbers: false, shelfLifeDays: 0)
            .Error.Code.ShouldBe("Product.ShelfLifeNotPositive");
    }

    [Fact]
    public void Only_stocked_items_are_held_and_valued()
    {
        Create(itemType: ItemType.Stock).IsStocked.ShouldBeTrue();
        Create(itemType: ItemType.Service).IsStocked.ShouldBeFalse();
        Create(itemType: ItemType.NonStock).IsStocked.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ barcodes

    [Fact]
    public void A_barcode_inherits_the_products_rates_unless_given_its_own()
    {
        // A multipack scanned at the till is the same product at a different price,
        // which is what the multiple-rate grid is for.
        Product product = Create();
        product.SetRates(Rates(cost: 400m, retail: 550m));

        ProductBarcode single = product.AddBarcode("8801643000001").Value;
        ProductBarcode multipack = product
            .AddBarcode("8801643000002", Rates(cost: 1_150m, retail: 1_500m)).Value;

        single.Rates.RetailRate.ShouldBe(550m);
        multipack.Rates.RetailRate.ShouldBe(1_500m);
        product.Barcodes.Count.ShouldBe(2);
    }

    [Fact]
    public void The_same_barcode_cannot_be_added_to_one_product_twice()
    {
        Product product = Create();
        product.AddBarcode("8801643000001");

        Result<ProductBarcode> result = product.AddBarcode("  8801643000001  ");

        result.Error.Code.ShouldBe("Product.DuplicateBarcode");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        product.Barcodes.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_barcode_can_be_removed()
    {
        Product product = Create();
        ProductBarcode barcode = product.AddBarcode("8801643000001").Value;

        product.RemoveBarcode(barcode.Id).IsSuccess.ShouldBeTrue();
        product.Barcodes.ShouldBeEmpty();
    }

    [Fact]
    public void Removing_a_barcode_that_is_not_there_is_reported()
    {
        Create().RemoveBarcode(ProductBarcodeId.NewId())
            .Error.Code.ShouldBe("Product.BarcodeNotFound");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_barcode_cannot_be_blank(string barcode)
    {
        Create().AddBarcode(barcode).Error.Code.ShouldBe("Product.BarcodeRequired");
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public void A_discontinued_product_is_still_sellable_from_stock()
    {
        // Distinct from inactive, and both are needed. Collapsing them would force a
        // choice between selling remaining stock and keeping it off new orders.
        Product product = Create();
        product.Discontinue();

        product.IsDiscontinued.ShouldBeTrue();
        product.IsTransactable.ShouldBeTrue();
        product.EnsureTransactable().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_inactive_product_cannot_be_put_on_a_document()
    {
        Product product = Create();
        product.Deactivate();

        product.EnsureTransactable().Error.Code.ShouldBe("Product.Inactive");

        product.Activate();
        product.EnsureTransactable().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_reinstated_product_returns_to_the_range()
    {
        Product product = Create();
        product.Discontinue();
        product.Reinstate();

        product.IsDiscontinued.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ attributes

    [Fact]
    public void Device_attributes_are_kept_as_they_are_printed()
    {
        // Parsing "128GB" into a number would mean choosing units, handling the ones
        // that do not fit, and presenting them back differently from the box.
        Product product = Create();
        DeviceAttributes attributes = DeviceAttributes
            .Create("Galaxy A54", "Awesome Graphite", "5000mAh", "8GB", "256GB").Value;

        product.SetDeviceAttributes(attributes);

        product.Device.Storage.ShouldBe("256GB");
        product.Device.Battery.ShouldBe("5000mAh");
    }

    [Fact]
    public void A_device_attribute_is_bounded()
    {
        DeviceAttributes
            .Create(new string('X', DeviceAttributes.MaximumLength + 1), null, null, null, null)
            .Error.Code.ShouldBe("Product.DeviceAttributeTooLong");
    }

    [Fact]
    public void Blank_device_attributes_are_stored_as_nothing_rather_than_empty_strings()
    {
        DeviceAttributes attributes = DeviceAttributes
            .Create("  ", null, string.Empty, "  8GB ", null).Value;

        attributes.Device.ShouldBeNull();
        attributes.Battery.ShouldBeNull();
        attributes.Ram.ShouldBe("8GB");
    }

    [Fact]
    public void A_product_can_be_reclassified_within_its_own_firm()
    {
        Product product = Create();
        Category accessories = Category.CreateRoot(Tenant, Firm, "ACC", "Accessories").Value;

        product.ReclassifyTo(accessories).IsSuccess.ShouldBeTrue();
        product.CategoryId.ShouldBe(accessories.Id);
    }

    [Fact]
    public void A_product_cannot_be_reclassified_into_another_firm()
    {
        Product product = Create();
        Category theirs = Category.CreateRoot(Tenant, FirmId.NewId(), "ACC", "Accessories").Value;

        product.ReclassifyTo(theirs).Error.Code.ShouldBe("Product.CategoryFromAnotherFirm");
    }

    [Fact]
    public void A_brand_from_another_firm_is_refused()
    {
        Product product = Create();
        Brand theirs = Brand.Create(Tenant, FirmId.NewId(), "SAMSUNG", "Samsung").Value;

        product.SetBrand(theirs).Error.Code.ShouldBe("Product.BrandFromAnotherFirm");
    }

    [Fact]
    public void A_brand_can_be_cleared()
    {
        Product product = Create();
        product.SetBrand(Brand.Create(Tenant, Firm, "SAMSUNG", "Samsung").Value);

        product.SetBrand(null).IsSuccess.ShouldBeTrue();
        product.BrandId.ShouldBeNull();
    }

    // ------------------------------------------------------------------ helpers

    private static Category RootCategory() =>
        Category.CreateRoot(Tenant, Firm, "PHONES", "Phones").Value;

    private static UnitOfMeasure BaseUnit() =>
        UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

    private static Result<Product> TryCreate(string code, string description) =>
        Product.Create(RootCategory(), BaseUnit(), code, description, ItemType.Stock, Qar);

    private static Product Create(
        string code = "PRO-1004",
        string description = "Galaxy A54",
        ItemType itemType = ItemType.Stock,
        UnitOfMeasure? stockUnit = null) =>
        Product.Create(
            RootCategory(), stockUnit ?? BaseUnit(), code, description, itemType, Qar).Value;

    private static ProductRates Rates(decimal cost, decimal retail) =>
        ProductRates.Create(cost, 0m, 0m, retail, retail, retail, 0m).Value;
}
