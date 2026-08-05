using ERP.Domain.Identity;

namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>
/// The permissions the shipped screens check, and the roles that hold them.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>starting</em> catalogue, not a closed set. The specification
/// requires permissions to be configurable from the database, so administrators
/// may add more for anything they configure themselves. Seeding is idempotent:
/// a permission already present is left alone, and one added here later appears
/// on the next start without disturbing existing grants.
/// </para>
/// <para>
/// Codes are <c>module:resource:verb</c>. Grouping by module matters because the
/// dynamic menu system assigns menus per module, and the permission a menu item
/// requires is resolved from the same taxonomy.
/// </para>
/// </remarks>
internal static class PermissionCatalogue
{
    /// <summary>Every verb, for resources where the full set applies.</summary>
    private static readonly PermissionVerb[] AllVerbs =
    [
        PermissionVerb.View,
        PermissionVerb.Create,
        PermissionVerb.Edit,
        PermissionVerb.Delete,
        PermissionVerb.Approve,
        PermissionVerb.Print,
        PermissionVerb.Export,
    ];

    private static readonly PermissionVerb[] ReadOnly =
    [
        PermissionVerb.View,
        PermissionVerb.Print,
        PermissionVerb.Export,
    ];

    private static readonly PermissionVerb[] MasterData =
    [
        PermissionVerb.View,
        PermissionVerb.Create,
        PermissionVerb.Edit,
        PermissionVerb.Delete,
        PermissionVerb.Export,
    ];

    /// <summary>
    /// The resources the platform knows about, with the verbs each supports.
    /// </summary>
    /// <remarks>
    /// Verb sets differ by resource on purpose. A report cannot be approved, and a
    /// posted voucher cannot be edited in the way a master record can, so granting
    /// every verb everywhere would create permissions that mean nothing and invite
    /// an administrator to grant something the software will never check.
    /// </remarks>
    internal static IReadOnlyList<ResourceDefinition> Resources { get; } =
    [
        // ---------------------------------------------------------- platform
        new("platform", "tenant", ReadOnly, "The tenant's own registration and subscription"),
        new("platform", "firm", MasterData, "Firms"),
        new("platform", "branch", MasterData, "Branches and stock locations"),
        new("platform", "financial-year", MasterData, "Financial years"),
        new("platform", "user", MasterData, "Users"),
        new("platform", "role", MasterData, "Roles and their permissions"),
        new("platform", "permission", ReadOnly, "The permission catalogue"),
        new("platform", "menu", MasterData, "The dynamic menu tree"),
        new("platform", "setting", [PermissionVerb.View, PermissionVerb.Edit], "System settings"),
        new("platform", "audit-log", ReadOnly, "The audit trail"),
        new("platform", "numbering-series", MasterData, "Document numbering series"),

        // ---------------------------------------------------------- accounting
        new("accounting", "account-group", MasterData, "Account groups"),
        new("accounting", "ledger", MasterData, "Ledgers and the chart of accounts"),
        new("accounting", "sub-ledger", MasterData, "Customers, suppliers, and employees as sub-ledgers"),
        new("accounting", "additional-ledger", MasterData, "Additional charge ledgers"),
        new("accounting", "voucher", AllVerbs, "Receipts, payments, journals, and contras"),
        new("accounting", "opening-balance", MasterData, "Opening balances"),
        new("accounting", "cheque", AllVerbs, "Cheque and post-dated cheque management"),
        new("accounting", "bill-allocation", [PermissionVerb.View, PermissionVerb.Edit], "Bill-wise settlement"),
        new("accounting", "currency", MasterData, "Currencies and exchange rates"),
        new("accounting", "report", ReadOnly, "Accounting reports"),

        // ---------------------------------------------------------- inventory
        new("inventory", "product", MasterData, "The product master"),
        new("inventory", "category", MasterData, "Categories, sub-classes, and brands"),
        new("inventory", "unit", MasterData, "Units of measurement"),
        new("inventory", "warehouse", MasterData, "Warehouses, racks, and bins"),
        new("inventory", "batch", [PermissionVerb.View, PermissionVerb.Create, PermissionVerb.Edit, PermissionVerb.Export], "Batches and expiry"),
        new("inventory", "serial-number", [PermissionVerb.View, PermissionVerb.Create, PermissionVerb.Edit, PermissionVerb.Export], "Serial numbers and warranties"),
        new("inventory", "stock-adjustment", AllVerbs, "Stock adjustments and damaged stock"),
        new("inventory", "stock-transfer", AllVerbs, "Stock transfers between locations"),
        new("inventory", "physical-stock", AllVerbs, "Physical stock verification"),
        new("inventory", "report", ReadOnly, "Inventory reports"),

        // ---------------------------------------------------------- sales
        new("sales", "quotation", AllVerbs, "Sales quotations"),
        new("sales", "order", AllVerbs, "Sales orders"),
        new("sales", "delivery-note", AllVerbs, "Delivery notes"),
        new("sales", "invoice", AllVerbs, "Sales invoices"),
        new("sales", "return", AllVerbs, "Sales returns"),
        new("sales", "customer", MasterData, "Customers"),
        new("sales", "loyalty", [PermissionVerb.View, PermissionVerb.Edit], "Loyalty points and privilege cards"),
        new("sales", "discount", [PermissionVerb.View, PermissionVerb.Edit], "Discounts on a document"),
        new("sales", "report", ReadOnly, "Sales reports"),

        // ---------------------------------------------------------- purchase
        new("purchase", "requisition", AllVerbs, "Purchase requisitions"),
        new("purchase", "order", AllVerbs, "Purchase orders"),
        new("purchase", "goods-receipt", AllVerbs, "Goods receipts"),
        new("purchase", "invoice", AllVerbs, "Purchase invoices"),
        new("purchase", "return", AllVerbs, "Purchase returns"),
        new("purchase", "supplier", MasterData, "Suppliers"),
        new("purchase", "report", ReadOnly, "Purchase reports"),

        // ---------------------------------------------------------- manufacturing
        new("manufacturing", "bom", MasterData, "Bills of materials"),
        new("manufacturing", "production", AllVerbs, "Production entries"),
        new("manufacturing", "report", ReadOnly, "Manufacturing reports"),

        // ---------------------------------------------------------- service
        new("service", "job-card", AllVerbs, "Service job cards"),
        new("service", "estimate", AllVerbs, "Service estimates"),
        new("service", "invoice", AllVerbs, "Service invoices"),
        new("service", "rma", AllVerbs, "Return merchandise authorisations"),
        new("service", "technician", MasterData, "Technicians and service centres"),
        new("service", "report", ReadOnly, "Service reports"),

        // ---------------------------------------------------------- cross-cutting
        new("reporting", "report-definition", MasterData, "The dynamic report builder"),
        new("reporting", "dashboard", MasterData, "Dashboards and widgets"),
        new("printing", "template", MasterData, "Print designer templates"),
        new("workflow", "definition", MasterData, "Workflow definitions"),
    ];

    /// <summary>
    /// The seeded roles from the specification, and what each may do.
    /// </summary>
    /// <remarks>
    /// Grants are expressed as module and verb patterns rather than an explicit
    /// list of several hundred codes. Adding a resource to a module therefore
    /// extends the roles that already work in that module, which is almost always
    /// what is wanted and avoids a role silently losing coverage as the system
    /// grows.
    /// <para>
    /// Super Administrator is the exception: it holds
    /// <see cref="Role.GrantsAllPermissions"/> instead of an enumerated set, so a
    /// newly-added permission never needs granting to it.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new(
            "Super Administrator",
            "Unrestricted access across every firm and branch, including platform configuration.",
            GrantsEverything: true,
            Grants: []),

        new(
            "Firm Administrator",
            "Full access within the assigned firm, excluding platform-level configuration.",
            GrantsEverything: false,
            Grants:
            [
                new("platform", ["firm", "branch", "financial-year", "user", "role", "menu", "setting", "numbering-series", "audit-log", "permission"], AllVerbs),
                new("accounting", null, AllVerbs),
                new("inventory", null, AllVerbs),
                new("sales", null, AllVerbs),
                new("purchase", null, AllVerbs),
                new("manufacturing", null, AllVerbs),
                new("service", null, AllVerbs),
                new("reporting", null, AllVerbs),
                new("printing", null, AllVerbs),
                new("workflow", null, AllVerbs),
            ]),

        new(
            "Branch Manager",
            "Operational control of one branch: approves documents but cannot alter firm-level configuration.",
            GrantsEverything: false,
            Grants:
            [
                new("platform", ["branch", "user"], ReadOnly),
                new("accounting", ["voucher", "cheque", "bill-allocation", "report"], AllVerbs),
                new("accounting", ["ledger", "sub-ledger"], ReadOnly),
                new("inventory", null, AllVerbs),
                new("sales", null, AllVerbs),
                new("purchase", null, AllVerbs),
                new("service", null, AllVerbs),
                new("reporting", ["dashboard"], ReadOnly),
            ]),

        new(
            "Accountant",
            "The accounting module in full, with read access to the documents that post into it.",
            GrantsEverything: false,
            Grants:
            [
                new("accounting", null, AllVerbs),
                new("sales", ["invoice", "return", "customer", "report"], ReadOnly),
                new("purchase", ["invoice", "return", "supplier", "report"], ReadOnly),
                new("service", ["invoice", "report"], ReadOnly),
                new("inventory", ["report"], ReadOnly),
                new("reporting", ["dashboard"], ReadOnly),
            ]),

        new(
            "Sales Executive",
            "Raises sales documents. Cannot approve them, delete them, or see cost and margin.",
            GrantsEverything: false,
            Grants:
            [
                // No Approve and no Delete: a salesperson who can approve their own
                // invoice defeats the point of an approval step, and one who can
                // delete a document can erase the evidence of a mistake.
                new("sales", ["quotation", "order", "delivery-note", "invoice", "customer", "loyalty"],
                    [PermissionVerb.View, PermissionVerb.Create, PermissionVerb.Edit, PermissionVerb.Print]),
                new("sales", ["report"], [PermissionVerb.View, PermissionVerb.Print]),
                new("inventory", ["product", "batch", "serial-number"], [PermissionVerb.View]),
                new("service", ["job-card"], [PermissionVerb.View, PermissionVerb.Create]),
            ]),

        new(
            "Store Keeper",
            "Physical stock movement. No pricing, no customer accounts, no financial postings.",
            GrantsEverything: false,
            Grants:
            [
                new("inventory", ["stock-adjustment", "stock-transfer", "physical-stock"],
                    [PermissionVerb.View, PermissionVerb.Create, PermissionVerb.Edit, PermissionVerb.Print]),
                new("inventory", ["product", "category", "unit", "warehouse", "batch", "serial-number"],
                    [PermissionVerb.View]),
                new("inventory", ["report"], ReadOnly),
                new("purchase", ["goods-receipt"], [PermissionVerb.View, PermissionVerb.Create, PermissionVerb.Print]),
                new("sales", ["delivery-note"], [PermissionVerb.View, PermissionVerb.Print]),
            ]),
    ];

    /// <summary>One resource and the verbs it supports.</summary>
    /// <param name="Module">The owning module.</param>
    /// <param name="Resource">The resource name.</param>
    /// <param name="Verbs">The verbs that are meaningful for it.</param>
    /// <param name="Description">A human-readable description.</param>
    internal sealed record ResourceDefinition(
        string Module,
        string Resource,
        IReadOnlyList<PermissionVerb> Verbs,
        string Description);

    /// <summary>A seeded role and the permissions it holds.</summary>
    /// <param name="Name">The role name.</param>
    /// <param name="Description">What the role is for.</param>
    /// <param name="GrantsEverything">
    /// Whether the role implicitly holds every permission, present and future.
    /// </param>
    /// <param name="Grants">The permission patterns granted.</param>
    internal sealed record RoleDefinition(
        string Name,
        string Description,
        bool GrantsEverything,
        IReadOnlyList<GrantPattern> Grants);

    /// <summary>A pattern selecting permissions to grant.</summary>
    /// <param name="Module">The module to grant within.</param>
    /// <param name="ResourceNames">
    /// The resources to include, or <see langword="null"/> for every resource in
    /// the module.
    /// </param>
    /// <param name="Verbs">The verbs to grant, where the resource supports them.</param>
    internal sealed record GrantPattern(
        string Module,
        IReadOnlyList<string>? ResourceNames,
        IReadOnlyList<PermissionVerb> Verbs);
}
