using ERP.Domain.Inventory;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Inventory;

/// <summary>
/// Tests for <see cref="Category"/>, <see cref="Brand"/> and <see cref="Warehouse"/>.
/// </summary>
/// <remarks>
/// Three masters that look like plain records and are mostly are. What is worth
/// pinning is the handful of places they are not: a category tree that must stay a
/// tree, and a default warehouse that has to remain both singular and usable.
/// </remarks>
public sealed class InventoryMasterTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    // ------------------------------------------------------------------ categories

    [Fact]
    public void A_sub_class_is_a_category_with_a_parent()
    {
        // The legacy ribbon names Category and Sub Class separately. They are one
        // thing here, which is what lets a third level exist the day somebody wants it.
        Category phones = RootCategory("PHONES", "Phones");
        Category android = Category.CreateChild(phones, "ANDROID", "Android").Value;

        android.ParentId.ShouldBe(phones.Id);
        android.FirmId.ShouldBe(phones.FirmId);
        android.TenantId.ShouldBe(phones.TenantId);
    }

    [Fact]
    public void A_category_cannot_be_placed_beneath_itself()
    {
        Category phones = RootCategory("PHONES", "Phones");

        phones.MoveTo(phones).Error.Code.ShouldBe("Category.CannotParentToSelf");
    }

    [Fact]
    public void A_category_cannot_be_placed_beneath_another_firms_category()
    {
        Category phones = RootCategory("PHONES", "Phones");

        Category elsewhere =
            Category.CreateRoot(Tenant, FirmId.NewId(), "OTHER", "Other").Value;

        phones.MoveTo(elsewhere).Error.Code.ShouldBe("Category.DifferentFirm");
    }

    [Fact]
    public void A_category_returns_to_the_top_level_when_moved_to_no_parent()
    {
        Category phones = RootCategory("PHONES", "Phones");
        Category android = Category.CreateChild(phones, "ANDROID", "Android").Value;

        android.MoveTo(null).IsSuccess.ShouldBeTrue();

        android.ParentId.ShouldBeNull();
    }

    [Fact]
    public void A_category_code_is_upper_cased()
    {
        RootCategory("phones", "Phones").Code.ShouldBe("PHONES");
    }

    // ---------------------------------------------------------------------- brands

    [Fact]
    public void A_brand_carries_both_languages()
    {
        Brand brand = Brand.Create(Tenant, Firm, "SAMSUNG", "Samsung").Value;
        brand.SetArabicName("سامسونج");

        brand.NameArabic.ShouldBe("سامسونج");
        brand.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Clearing_an_arabic_name_leaves_null_rather_than_blank()
    {
        Brand brand = Brand.Create(Tenant, Firm, "SAMSUNG", "Samsung").Value;

        brand.SetArabicName("سامسونج");
        brand.SetArabicName("   ");

        brand.NameArabic.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "Samsung", "Brand.CodeRequired")]
    [InlineData("SAMSUNG", "", "Brand.NameRequired")]
    public void A_brand_needs_a_code_and_a_name(string code, string name, string expected)
    {
        Brand.Create(Tenant, Firm, code, name).Error.Code.ShouldBe(expected);
    }

    // ------------------------------------------------------------------ warehouses

    [Fact]
    public void A_warehouse_may_belong_to_a_branch_or_to_none()
    {
        // A central store serving every branch is an ordinary arrangement.
        Warehouse central = Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;
        Warehouse branch = Warehouse
            .Create(Tenant, Firm, "DOHA", "Doha store", BranchId.NewId()).Value;

        central.BranchId.ShouldBeNull();
        branch.BranchId.ShouldNotBeNull();
    }

    [Fact]
    public void A_warehouse_can_be_made_the_default()
    {
        Warehouse store = Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;

        store.MakeDefault().IsSuccess.ShouldBeTrue();
        store.IsDefault.ShouldBeTrue();

        store.ClearDefault();
        store.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void An_inactive_warehouse_cannot_become_the_default()
    {
        // The default is what a document fills itself in with. Offering one nobody may
        // post to puts the error at the end of data entry rather than the start.
        Warehouse store = Warehouse.Create(Tenant, Firm, "OLD", "Old store").Value;
        store.Deactivate().IsSuccess.ShouldBeTrue();

        store.MakeDefault().Error.Code.ShouldBe("Warehouse.InactiveCannotBeDefault");
    }

    [Fact]
    public void The_default_warehouse_cannot_be_deactivated_while_it_holds_that_role()
    {
        // Otherwise every new document fills itself in with a location it may not use,
        // which reads as the software being broken rather than a setting needing changed.
        Warehouse store = Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;
        store.MakeDefault().IsSuccess.ShouldBeTrue();

        store.Deactivate().Error.Code.ShouldBe("Warehouse.DefaultCannotBeDeactivated");

        store.ClearDefault();
        store.Deactivate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_warehouse_records_where_it_is()
    {
        Warehouse store = Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;

        store.SetAddress("Salwa Road, Doha").IsSuccess.ShouldBeTrue();
        store.Address.ShouldBe("Salwa Road, Doha");

        store.SetAddress(null).IsSuccess.ShouldBeTrue();
        store.Address.ShouldBeNull();
    }

    [Fact]
    public void An_over_long_address_is_refused()
    {
        Warehouse store = Warehouse.Create(Tenant, Firm, "MAIN", "Main store").Value;

        Result result = store.SetAddress(new string('x', Warehouse.MaximumAddressLength + 1));

        result.Error.Code.ShouldBe("Warehouse.AddressTooLong");
    }

    private static Category RootCategory(string code, string name) =>
        Category.CreateRoot(Tenant, Firm, code, name).Value;
}
