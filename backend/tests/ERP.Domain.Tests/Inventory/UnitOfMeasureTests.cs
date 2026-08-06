using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Inventory;

/// <summary>
/// Tests for <see cref="UnitOfMeasure"/>.
/// </summary>
/// <remarks>
/// The specification's rule is that a product measured in <c>No</c> may be entered in
/// <c>Pack</c> or <c>Box</c> and never in <c>Litre</c>, and that is most of what these
/// cover. The other half is the shape that makes the rule cheap to enforce: units form
/// flat groups, so "same group" is one comparison and every conversion is a single
/// multiplication rather than a walk that compounds its rounding.
/// </remarks>
public sealed class UnitOfMeasureTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void A_base_unit_is_its_own_group_and_converts_one_for_one()
    {
        UnitOfMeasure each = Base("NO", "Number");

        each.IsBaseUnit.ShouldBeTrue();
        each.ConversionFactor.ShouldBe(1m);
        each.GroupId.ShouldBe(each.Id);
        each.BaseUnitId.ShouldBeNull();
    }

    [Fact]
    public void A_derived_unit_belongs_to_its_bases_group()
    {
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure pack = Derived(each, "PACK", "Pack of 12", 12m);

        pack.IsBaseUnit.ShouldBeFalse();
        pack.GroupId.ShouldBe(each.Id);
        pack.IsInSameGroupAs(each).ShouldBeTrue();
    }

    [Fact]
    public void Two_units_of_the_same_base_are_in_one_group()
    {
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure pack = Derived(each, "PACK", "Pack of 12", 12m);
        UnitOfMeasure box = Derived(each, "BOX", "Box of 24", 24m);

        pack.IsInSameGroupAs(box).ShouldBeTrue();
    }

    [Fact]
    public void A_count_and_a_volume_are_not_in_one_group()
    {
        // The rule the specification states outright. Nothing relates a count to a
        // volume, and a system that guessed at one would be inventing stock.
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure litre = Base("LTR", "Litre", decimalPlaces: 3);

        each.IsInSameGroupAs(litre).ShouldBeFalse();
    }

    [Fact]
    public void A_unit_cannot_be_derived_from_another_derived_unit()
    {
        // Refusing the chain is what keeps every conversion a single multiplication.
        // A Box defined as two Packs of twelve would compound its factor, and with a
        // fractional factor that is where the rounding error comes from.
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure pack = Derived(each, "PACK", "Pack of 12", 12m);

        Result<UnitOfMeasure> result =
            UnitOfMeasure.CreateDerived(pack, "BOX", "Box of 2 packs", 2m);

        result.Error.Code.ShouldBe("UnitOfMeasure.NotABaseUnit");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_conversion_factor_must_be_positive(decimal factor)
    {
        UnitOfMeasure each = Base("NO", "Number");

        UnitOfMeasure.CreateDerived(each, "BAD", "Bad", factor)
            .Error.Code.ShouldBe("UnitOfMeasure.FactorNotPositive");
    }

    [Fact]
    public void A_quantity_converts_between_units_of_the_same_group()
    {
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure pack = Derived(each, "PACK", "Pack of 12", 12m);
        UnitOfMeasure box = Derived(each, "BOX", "Box of 24", 24m);

        UnitOfMeasure.Convert(2m, box, each).Value.ShouldBe(48m);
        UnitOfMeasure.Convert(48m, each, box).Value.ShouldBe(2m);
        UnitOfMeasure.Convert(2m, box, pack).Value.ShouldBe(4m);
    }

    [Fact]
    public void A_conversion_across_groups_is_refused()
    {
        UnitOfMeasure each = Base("NO", "Number");
        UnitOfMeasure litre = Base("LTR", "Litre", decimalPlaces: 3);

        UnitOfMeasure.Convert(1m, each, litre)
            .Error.Code.ShouldBe("UnitOfMeasure.DifferentGroups");
    }

    [Fact]
    public void A_fractional_factor_survives_a_round_trip()
    {
        // A third of a base unit is a legitimate pack size, and it is exactly the case
        // a chained conversion would lose. One hop each way returns the original.
        UnitOfMeasure kilo = Base("KG", "Kilogram", decimalPlaces: 3);
        UnitOfMeasure third = Derived(kilo, "THIRD", "Third of a kilo", 1m / 3m);

        decimal there = UnitOfMeasure.Convert(3m, third, kilo).Value;
        decimal back = UnitOfMeasure.Convert(there, kilo, third).Value;

        back.ShouldBe(3m, tolerance: 0.0000001m);
    }

    [Fact]
    public void A_unit_counted_rather_than_measured_refuses_a_fractional_quantity()
    {
        // Half a bottle is a quantity; half a serial-numbered handset is a data-entry
        // error, and the unit is where that gets caught.
        UnitOfMeasure each = Base("NO", "Number");

        each.EnsurePrecision(3m).IsSuccess.ShouldBeTrue();
        each.EnsurePrecision(2.5m).Error.Code.ShouldBe("UnitOfMeasure.TooPrecise");
    }

    [Fact]
    public void A_measured_unit_accepts_quantities_to_its_stated_precision()
    {
        UnitOfMeasure kilo = Base("KG", "Kilogram", decimalPlaces: 3);

        kilo.EnsurePrecision(1.125m).IsSuccess.ShouldBeTrue();
        kilo.EnsurePrecision(1.1255m).Error.Code.ShouldBe("UnitOfMeasure.TooPrecise");
    }

    [Fact]
    public void Trailing_zeros_are_not_mistaken_for_precision()
    {
        // 1.10 and 1.1 are the same number carrying different scales. Refusing the
        // first would reject a quantity that is exactly representable.
        UnitOfMeasure kilo = Base("KG", "Kilogram", decimalPlaces: 1);

        kilo.EnsurePrecision(1.10m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_code_is_upper_cased_so_it_identifies_one_unit_however_it_was_typed()
    {
        Base("kg", "Kilogram").Code.ShouldBe("KG");
    }

    [Fact]
    public void A_unit_is_deactivated_rather_than_deleted()
    {
        // Documents already entered in it must go on meaning what they meant.
        UnitOfMeasure each = Base("NO", "Number");

        each.Deactivate();
        each.IsActive.ShouldBeFalse();

        each.Activate();
        each.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "Number", "UnitOfMeasure.CodeRequired")]
    [InlineData("NO", "", "UnitOfMeasure.NameRequired")]
    public void A_code_and_a_name_are_both_required(
        string code,
        string name,
        string expected)
    {
        UnitOfMeasure.CreateBase(Tenant, Firm, code, name).Error.Code.ShouldBe(expected);
    }

    [Fact]
    public void More_decimal_places_than_a_quantity_can_hold_are_refused()
    {
        UnitOfMeasure.CreateBase(Tenant, Firm, "X", "Too precise", decimalPlaces: 7)
            .Error.Code.ShouldBe("UnitOfMeasure.DecimalPlacesOutOfRange");
    }

    private static UnitOfMeasure Base(string code, string name, int decimalPlaces = 0) =>
        UnitOfMeasure.CreateBase(Tenant, Firm, code, name, decimalPlaces: decimalPlaces).Value;

    private static UnitOfMeasure Derived(
        UnitOfMeasure baseUnit,
        string code,
        string name,
        decimal factor) =>
        UnitOfMeasure.CreateDerived(baseUnit, code, name, factor).Value;
}
