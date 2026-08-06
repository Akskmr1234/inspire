using ERP.Application.Abstractions.Security;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Menus;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Tests.Platform.Menus;

/// <summary>
/// Tests for <see cref="GetMenuQueryHandler"/>.
/// </summary>
/// <remarks>
/// The menu is stored flat and rendered as a tree, and the handler is what turns one
/// into the other while dropping everything the caller cannot reach. Three behaviours
/// carry the weight: that an entry the user lacks the permission for disappears, that
/// a heading left empty by that filtering disappears with it, and that the wildcard a
/// super administrator holds is understood - because the account that can reach
/// everything is also the one that would notice an empty menu first.
/// </remarks>
public sealed class GetMenuTests
{
    [Fact]
    public async Task The_tree_is_assembled_from_flat_rows_in_sort_order()
    {
        Fixture fixture = new();
        fixture.Heading("reports", "Reports", sortOrder: 20);
        fixture.Heading("transactions", "Transactions", sortOrder: 10);
        fixture.Link("reports.b", "B", "/b", parentCode: "reports", sortOrder: 20);
        fixture.Link("reports.a", "A", "/a", parentCode: "reports", sortOrder: 10);

        // Each heading needs something beneath it, or it is pruned as an empty
        // promise - which is the subject of its own test below.
        fixture.Link("transactions.entry", "Entry", "/entry", parentCode: "transactions");

        MenuResponse menu = (await fixture.Handle()).Value;

        menu.Items.Select(i => i.Code).ShouldBe(["transactions", "reports"]);
        menu.Items[1].Children.Select(c => c.Code).ShouldBe(["reports.a", "reports.b"]);
    }

    [Fact]
    public async Task An_entry_whose_permission_is_not_held_is_left_out()
    {
        Fixture fixture = new("accounting:report:view");
        fixture.Heading("reports", "Reports");
        fixture.Link(
            "reports.trial-balance", "Trial balance", "/tb", parentCode: "reports",
            permission: "accounting:report:view");
        fixture.Link(
            "reports.payroll", "Payroll", "/pay", parentCode: "reports",
            permission: "payroll:report:view");

        MenuResponse menu = (await fixture.Handle()).Value;

        menu.Items.ShouldHaveSingleItem().Children
            .ShouldHaveSingleItem().Code.ShouldBe("reports.trial-balance");
    }

    [Fact]
    public async Task A_heading_left_empty_by_filtering_disappears_with_its_children()
    {
        // An empty heading advertises something the user cannot reach and invites them
        // to ask why it does nothing.
        Fixture fixture = new("accounting:report:view");
        fixture.Heading("reports", "Reports");
        fixture.Link(
            "reports.trial-balance", "Trial balance", "/tb", parentCode: "reports",
            permission: "accounting:report:view");
        fixture.Heading("inventory", "Inventory");
        fixture.Link(
            "inventory.stock", "Stock", "/stock", parentCode: "inventory",
            permission: "inventory:report:view");

        MenuResponse menu = (await fixture.Handle()).Value;

        menu.Items.Select(i => i.Code).ShouldBe(["reports"]);
    }

    [Fact]
    public async Task A_heading_with_a_route_of_its_own_survives_losing_its_children()
    {
        // It is still somewhere to go, so it is not an empty promise.
        Fixture fixture = new();
        fixture.Link("dashboard", "Dashboard", "/dashboard");
        fixture.Link(
            "dashboard.sales", "Sales", "/dashboard/sales", parentCode: "dashboard",
            permission: "sales:report:view");

        MenuResponse menu = (await fixture.Handle()).Value;

        MenuEntry entry = menu.Items.ShouldHaveSingleItem();
        entry.Code.ShouldBe("dashboard");
        entry.Children.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_entry_requiring_nothing_is_shown_to_everybody()
    {
        Fixture fixture = new();
        fixture.Link("home", "Home", "/");

        (await fixture.Handle()).Value.Items.ShouldHaveSingleItem().Code.ShouldBe("home");
    }

    [Fact]
    public async Task The_wildcard_a_super_administrator_holds_opens_everything()
    {
        // Their permission list is ["*"], not several hundred codes. Testing set
        // membership alone would hide the entire menu from the one account that can
        // reach every screen in the system.
        Fixture fixture = new("*");
        fixture.Heading("reports", "Reports");
        fixture.Link(
            "reports.a", "A", "/a", parentCode: "reports", permission: "anything:at:all");
        fixture.Link(
            "reports.b", "B", "/b", parentCode: "reports", permission: "something:else:entirely");

        MenuResponse menu = (await fixture.Handle()).Value;

        menu.Items.ShouldHaveSingleItem().Children.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Nesting_goes_deeper_than_two_levels()
    {
        Fixture fixture = new();
        fixture.Heading("masters", "Masters");
        fixture.Heading("masters.inventory", "Inventory masters", parentCode: "masters");
        fixture.Link(
            "masters.inventory.items", "Items", "/items", parentCode: "masters.inventory");

        MenuResponse menu = (await fixture.Handle()).Value;

        menu.Items.ShouldHaveSingleItem()
            .Children.ShouldHaveSingleItem()
            .Children.ShouldHaveSingleItem()
            .Route.ShouldBe("/items");
    }

    [Fact]
    public async Task Resolving_a_menu_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<MenuResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Menu.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Resolving_a_menu_for_nobody_is_refused()
    {
        // Platform work runs as the system actor, which is not a person and has no
        // menu. UserId is never null, so this has to be checked rather than inferred.
        Fixture fixture = new(signedIn: false);

        Result<MenuResponse> result = await fixture.Handle();

        result.Error.Code.ShouldBe("Menu.NotSignedIn");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    /// <summary>A handler over a reader returning whatever rows a test registers.</summary>
    private sealed class Fixture
    {
        private readonly List<MenuItemRow> _rows = [];
        private readonly Dictionary<string, Guid> _idsByCode = new(StringComparer.Ordinal);
        private readonly GetMenuQueryHandler _handler;

        internal Fixture(
            string? heldPermission = null,
            bool firmSelected = true,
            bool signedIn = true)
        {
            FirmId firmId = FirmId.NewId();
            UserId userId = UserId.NewId();

            IMenuReader reader = Substitute.For<IMenuReader>();
            reader
                .ReadAsync(Arg.Any<FirmId>(), Arg.Any<CancellationToken>())
                .Returns(_ => _rows);

            IPermissionChecker permissions = Substitute.For<IPermissionChecker>();
            permissions
                .GetPermissionsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlySet<string>)(heldPermission is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>([heldPermission], StringComparer.Ordinal)));

            ICurrentUser currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(signedIn);
            currentUser.UserId.Returns(userId);

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(TenantId.NewId());
            tenant.FirmId.Returns(firmSelected ? firmId : null);

            _handler = new GetMenuQueryHandler(reader, permissions, currentUser, tenant);
        }

        /// <summary>Registers a heading, which navigates nowhere itself.</summary>
        internal void Heading(
            string code,
            string label,
            string? parentCode = null,
            int sortOrder = 0,
            string? permission = null) =>
            Add(code, label, route: null, parentCode, sortOrder, permission);

        /// <summary>Registers an entry that opens a screen.</summary>
        internal void Link(
            string code,
            string label,
            string route,
            string? parentCode = null,
            int sortOrder = 0,
            string? permission = null) =>
            Add(code, label, route, parentCode, sortOrder, permission);

        private void Add(
            string code,
            string label,
            string? route,
            string? parentCode,
            int sortOrder,
            string? permission)
        {
            Guid id = Guid.CreateVersion7();
            _idsByCode[code] = id;

            _rows.Add(new MenuItemRow(
                id,
                parentCode is null ? null : _idsByCode[parentCode],
                code,
                label,
                null,
                null,
                route,
                "accounting",
                sortOrder,
                permission));
        }

        internal Task<Result<MenuResponse>> Handle() =>
            _handler.Handle(new GetMenuQuery(), TestContext.Current.CancellationToken);
    }
}
