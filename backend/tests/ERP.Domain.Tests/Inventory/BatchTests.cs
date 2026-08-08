using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>Tests for <see cref="Batch"/>: identity, dates, and generated numbers.</summary>
/// <remarks>
/// Section 10. What these cover is the part of batch tracking that is decided once and
/// read forever afterwards - which number a lot carries, when it expires, and what it
/// was bought at - because every one of those is quoted back on a delivery note, an
/// expiry report, or a margin.
/// </remarks>
public sealed class BatchTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void A_batch_belongs_to_a_product_that_is_tracked_in_batches()
    {
        // A batch of a product nobody asked to track in batches would be a lot the
        // sales screen never offers and the position never consults: stock recorded
        // twice, in two places, one of them invisible.
        Batch.Open(Tenant, Firm, Untracked(), "A001").Error.Code
            .ShouldBe("Batch.NotTracked");

        Batch.Open(Tenant, Firm, Tracked(), "A001").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_number_is_required_and_kept_in_one_case()
    {
        Batch.Open(Tenant, Firm, Tracked(), "   ").Error.Code.ShouldBe("Batch.NumberRequired");

        Batch.Open(Tenant, Firm, Tracked(), new string('x', 41)).Error.Code
            .ShouldBe("Batch.NumberTooLong");

        // Upper-cased on the way in, like every other code in the system. A picker
        // reading "lot-7a" off a carton and a buyer who entered "LOT-7A" are holding
        // the same goods, and two rows for them would be two expiry dates.
        Batch.Open(Tenant, Firm, Tracked(), "  lot-7a ").Value.Number.ShouldBe("LOT-7A");
    }

    [Fact]
    public void Expiry_is_taken_from_the_shelf_life_when_it_is_not_given()
    {
        Product product = Tracked(shelfLifeDays: 90);
        DateOnly made = new(2026, 8, 1);

        Batch.Open(Tenant, Firm, product, "A001", made).Value.ExpiresOn
            .ShouldBe(new DateOnly(2026, 10, 30));

        // A printed date beats the arithmetic. The shelf life is a default for goods a
        // firm produces itself, not a rule about the ones it buys.
        Batch.Open(Tenant, Firm, product, "A002", made, new DateOnly(2026, 9, 15))
            .Value.ExpiresOn.ShouldBe(new DateOnly(2026, 9, 15));

        // Nothing to derive from: no manufacturing date, so no expiry.
        Batch.Open(Tenant, Firm, product, "A003").Value.ExpiresOn.ShouldBeNull();
    }

    [Fact]
    public void A_batch_cannot_expire_before_it_was_made()
    {
        Batch.Open(
                Tenant, Firm, Tracked(), "A001",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 1))
            .Error.Code.ShouldBe("Batch.ExpiryBeforeManufacture");
    }

    [Fact]
    public void Dates_can_be_corrected_afterwards()
    {
        // The one thing about a batch that changes after goods have moved, and it
        // earns the exception: the alternative to correcting a mistyped expiry date is
        // writing the lot off and receiving it again.
        Batch batch = Batch.Open(Tenant, Firm, Tracked(), "A001").Value;

        batch.SetDates(new DateOnly(2026, 8, 1), new DateOnly(2027, 8, 1))
            .IsSuccess.ShouldBeTrue();
        batch.ExpiresOn.ShouldBe(new DateOnly(2027, 8, 1));

        batch.SetDates(new DateOnly(2026, 8, 1), new DateOnly(2025, 1, 1))
            .Error.Code.ShouldBe("Batch.ExpiryBeforeManufacture");

        // Refused, and refused without changing anything.
        batch.ExpiresOn.ShouldBe(new DateOnly(2027, 8, 1));
    }

    [Fact]
    public void The_purchase_rate_is_recorded_once_and_not_restated()
    {
        // A later receipt into the same batch at another price changes what the
        // warehouse carries it at. It does not change what the first delivery was
        // bought for, and letting it would restate the margin on everything already
        // sold out of the lot.
        Batch batch = Batch.Open(Tenant, Firm, Tracked(), "A001", purchaseRate: 5m).Value;

        batch.RecordPurchaseRate(9m).IsSuccess.ShouldBeTrue();
        batch.PurchaseRate.ShouldBe(5m);

        Batch uncosted = Batch.Open(Tenant, Firm, Tracked(), "A002").Value;

        uncosted.RecordPurchaseRate(9m).IsSuccess.ShouldBeTrue();
        uncosted.PurchaseRate.ShouldBe(9m);

        uncosted.RecordPurchaseRate(-1m).Error.Code.ShouldBe("Batch.RateNegative");
    }

    [Fact]
    public void Expiry_is_judged_inclusively_and_a_batch_without_one_never_expires()
    {
        // The date on the carton is the last day the goods are good.
        Batch batch = Batch.Open(
            Tenant, Firm, Tracked(), "A001", expiresOn: new DateOnly(2026, 8, 30)).Value;

        batch.HasExpiredBy(new DateOnly(2026, 8, 30)).ShouldBeFalse();
        batch.HasExpiredBy(new DateOnly(2026, 8, 31)).ShouldBeTrue();

        Batch keeps = Batch.Open(Tenant, Firm, Tracked(), "A002").Value;

        keeps.HasExpiredBy(new DateOnly(2099, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Generated_numbers_run_a_thousand_to_a_letter()
    {
        Batch.NextNumber(null).Value.ShouldBe((1, "A001"));
        Batch.NextNumber(1).Value.ShouldBe((2, "A002"));

        // A999 is the last of the letter, and the next one is B001 rather than A1000:
        // the format section 10 asks for is four characters wide.
        Batch.NextNumber(998).Value.ShouldBe((999, "A999"));
        Batch.NextNumber(999).Value.ShouldBe((1000, "B001"));

        Batch.NextNumber(Batch.MaximumAutoSequence).Error.Code
            .ShouldBe("Batch.SequenceExhausted");
    }

    [Fact]
    public void A_typed_number_in_the_generated_format_takes_its_place_in_the_sequence()
    {
        // Somebody who enters A004 by hand has used that place whether or not the
        // system issued it. Generation that ignored them would offer A004 again and be
        // refused by the unique index, with nothing useful to say about why.
        Batch typed = Batch.Open(Tenant, Firm, Tracked(), "a004").Value;

        typed.AutoSequence.ShouldBe(4);
        typed.IsSequenced.ShouldBeTrue();

        Batch.NextNumber(typed.AutoSequence).Value.Number.ShouldBe("A005");
    }

    [Theory]
    [InlineData("A001", 1)]
    [InlineData("B001", 1000)]
    [InlineData("Z999", 25974)]
    [InlineData("A000", null)]
    [InlineData("A1", null)]
    [InlineData("A+12", null)]
    [InlineData("LOT-7A", null)]
    [InlineData("1234", null)]
    public void Only_the_generated_format_maps_onto_a_sequence(string number, int? sequence) =>
        Batch.SequenceOf(number).ShouldBe(sequence);

    [Fact]
    public void A_supplier_lot_code_holds_no_place_in_the_sequence()
    {
        Batch supplied = Batch.Open(Tenant, Firm, Tracked(), "PL/2026/0042").Value;

        supplied.AutoSequence.ShouldBeNull();
        supplied.IsSequenced.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ helpers

    private static Product Untracked() => Stocked();

    private static Product Tracked(int? shelfLifeDays = null)
    {
        Product product = Stocked();

        product.SetTracking(true, false, shelfLifeDays);

        return product;
    }

    private static Product Stocked()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

        return Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            each, $"PRO-{Guid.NewGuid():N}"[..12], "A thing", ItemType.Stock,
            CurrencyCode.Qar).Value;
    }
}
