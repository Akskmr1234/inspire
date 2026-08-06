using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tests.Platform;

/// <summary>
/// Tests for <see cref="MenuItem"/>.
/// </summary>
/// <remarks>
/// The menu is the one seeded structure users are expected to rearrange, so most of
/// what matters here is what they are allowed to rearrange it into. A system entry
/// must survive every edit except deletion, and a tree must stay a tree.
/// </remarks>
public sealed class MenuItemTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();

    [Fact]
    public void A_root_entry_starts_enabled()
    {
        MenuItem item = Root("reports", "Reports");

        item.IsEnabled.ShouldBeTrue();
        item.ParentId.ShouldBeNull();
        item.IsLink.ShouldBeFalse("an entry with no route is a heading");
    }

    [Fact]
    public void A_code_is_lower_cased_so_it_identifies_one_entry_however_it_was_typed()
    {
        Root("Accounts-Reports", "Reports").Code.ShouldBe("accounts-reports");
    }

    [Fact]
    public void A_child_inherits_its_parents_tenant_firm_and_module()
    {
        MenuItem parent = Root("reports", "Reports");

        MenuItem child = MenuItem.CreateChild(parent, "reports.tb", "Trial balance").Value;

        child.TenantId.ShouldBe(parent.TenantId);
        child.FirmId.ShouldBe(parent.FirmId);
        child.Module.ShouldBe(parent.Module);
        child.ParentId.ShouldBe(parent.Id);
    }

    [Fact]
    public void A_child_may_belong_to_a_different_module_from_where_it_is_shown()
    {
        // The specification asks for a report to be surfaced under another module. A
        // stock report shown under Accounts is still an inventory report.
        MenuItem parent = Root("accounts-reports", "Accounts reports");

        MenuItem child = MenuItem
            .CreateChild(parent, "accounts-reports.stock", "Stock", module: "inventory").Value;

        child.Module.ShouldBe("inventory");
    }

    [Fact]
    public void Setting_a_route_turns_a_heading_into_a_link_and_clearing_it_reverses_that()
    {
        MenuItem item = Root("reports", "Reports");

        item.SetRoute("/accounting/trial-balance").IsSuccess.ShouldBeTrue();
        item.IsLink.ShouldBeTrue();

        item.SetRoute(null).IsSuccess.ShouldBeTrue();
        item.IsLink.ShouldBeFalse();
    }

    [Fact]
    public void A_required_permission_is_lower_cased_to_match_the_catalogue()
    {
        MenuItem item = Root("reports", "Reports");

        item.RequirePermission("Accounting:Report:View");

        item.RequiredPermission.ShouldBe("accounting:report:view");
    }

    [Fact]
    public void Requiring_no_permission_shows_the_entry_to_everybody()
    {
        MenuItem item = Root("home", "Home");

        item.RequirePermission("accounting:report:view");
        item.RequirePermission("   ");

        item.RequiredPermission.ShouldBeNull();
    }

    [Fact]
    public void An_entry_cannot_be_placed_beneath_itself()
    {
        // The one cycle reachable in a single move, and it would render forever.
        MenuItem item = Root("reports", "Reports");

        Result result = item.MoveTo(item);

        result.Error.Code.ShouldBe("MenuItem.CannotParentToSelf");
    }

    [Fact]
    public void An_entry_cannot_be_moved_beneath_another_firms_entry()
    {
        MenuItem item = Root("reports", "Reports");

        MenuItem elsewhere = MenuItem
            .CreateRoot(Tenant, FirmId.NewId(), "other", "Other", "accounting").Value;

        item.MoveTo(elsewhere).Error.Code.ShouldBe("MenuItem.DifferentFirm");
    }

    [Fact]
    public void Moving_to_no_parent_returns_an_entry_to_the_top_level()
    {
        MenuItem parent = Root("reports", "Reports");
        MenuItem child = MenuItem.CreateChild(parent, "reports.tb", "Trial balance").Value;

        child.MoveTo(null).IsSuccess.ShouldBeTrue();

        child.ParentId.ShouldBeNull();
    }

    [Fact]
    public void A_system_entry_can_be_hidden_renamed_and_reordered_but_not_deleted()
    {
        // Everything the specification asks an administrator to be able to do, except
        // the one thing that would lose a screen with no way to find it again.
        MenuItem item = MenuItem
            .CreateRoot(Tenant, Firm, "reports", "Reports", "accounting", isSystem: true).Value;

        item.Rename("Financial reports").IsSuccess.ShouldBeTrue();
        item.Reorder(50).IsSuccess.ShouldBeTrue();
        item.Disable();

        item.Label.ShouldBe("Financial reports");
        item.SortOrder.ShouldBe(50);
        item.IsEnabled.ShouldBeFalse();
        item.EnsureDeletable().Error.Code.ShouldBe("MenuItem.SystemEntry");
    }

    [Fact]
    public void An_entry_an_administrator_added_may_be_deleted()
    {
        Root("custom", "Custom").EnsureDeletable().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_sort_order_is_refused()
    {
        Root("reports", "Reports").Reorder(-1).Error.Code
            .ShouldBe("MenuItem.SortOrderNegative");
    }

    [Theory]
    [InlineData("", "Reports", "MenuItem.CodeRequired")]
    [InlineData("reports", "", "MenuItem.LabelRequired")]
    public void A_code_and_a_label_are_both_required(
        string code,
        string label,
        string expectedError)
    {
        MenuItem.CreateRoot(Tenant, Firm, code, label, "accounting")
            .Error.Code.ShouldBe(expectedError);
    }

    private static MenuItem Root(string code, string label) =>
        MenuItem.CreateRoot(Tenant, Firm, code, label, "accounting").Value;
}
