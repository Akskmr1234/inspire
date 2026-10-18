namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>One entry in the default menu, as the catalogue describes it.</summary>
/// <param name="Code">The stable code, unique within a firm.</param>
/// <param name="Label">The English label.</param>
/// <param name="LabelArabic">The Arabic label.</param>
/// <param name="Module">The module the entry belongs to.</param>
/// <param name="Route">The client route, or null for a heading.</param>
/// <param name="RequiredPermission">
/// The <c>module:resource:verb</c> code needed to see it, or null for everybody.
/// </param>
/// <param name="Children">The entries beneath it.</param>
public sealed record MenuBlueprint(
    string Code,
    string Label,
    string LabelArabic,
    string Module,
    string? Route = null,
    string? RequiredPermission = null,
    IReadOnlyList<MenuBlueprint>? Children = null);

/// <summary>An entry moved from one heading to another by a later catalogue.</summary>
/// <param name="Code">The entry.</param>
/// <param name="FromParentCode">The heading an earlier catalogue put it under.</param>
/// <param name="ToParentCode">The heading it belongs under now.</param>
public sealed record MenuRelocation(string Code, string FromParentCode, string ToParentCode);

/// <summary>The menu every new firm starts with.</summary>
/// <remarks>
/// <para>
/// A starting point rather than a fixed structure. Every entry here is seeded as a
/// system entry, which means an administrator may rename, reorder, regroup, and hide
/// any of it, but not delete it - the screen behind an entry goes on existing whether
/// or not the menu mentions it, and a hidden entry is far easier to find again than a
/// deleted one.
/// </para>
/// <para>
/// The grouping follows the structure observed in the web reference: transactions
/// separated from the reports that read them, and cheques given their own heading
/// because they are a lifecycle somebody works through rather than a report they run.
/// Only screens that exist appear. A menu naming pages that are not built yet is a
/// list of complaints waiting to be filed.
/// </para>
/// </remarks>
public static class MenuCatalogue
{
    /// <summary>The permission every accounts report screen requires.</summary>
    private const string ViewReports = "accounting:report:view";

    /// <summary>The permission every stock report screen requires.</summary>
    private const string ViewInventoryReports = "inventory:report:view";

    /// <summary>Entries an earlier catalogue placed somewhere else, and where they go now.</summary>
    /// <remarks>
    /// Applied to existing firms at start-up, but only to an entry still under the
    /// heading the old catalogue gave it: one an administrator has moved stays where
    /// they put it.
    /// </remarks>
    public static IReadOnlyList<MenuRelocation> Relocations { get; } =
    [
        new("masters.products", "masters", "masters.inventory"),
        new("masters.categories", "masters", "masters.inventory"),
        new("masters.brands", "masters", "masters.inventory"),
        new("masters.units", "masters", "masters.inventory"),
        new("masters.warehouses", "masters", "masters.inventory"),
        new("masters.opening-stock", "masters", "masters.inventory"),
        new("masters.ledgers", "masters", "masters.accounts"),
        new("sales.customers", "sales", "masters.accounts"),
        new("purchase.suppliers", "purchase", "masters.accounts"),
    ];

    /// <summary>The default menu tree, in display order.</summary>
    public static IReadOnlyList<MenuBlueprint> Default { get; } =
    [
        // No permission: a dashboard is assigned to roles rather than gated, and the
        // screen itself says so when somebody has been given none.
        new(
            "dashboard",
            "Dashboard",
            "لوحة المعلومات",
            "platform",
            "/dashboard"),

        // Masters in two groups, the way a shop thinks of them: what it sells, and who
        // and what its money moves between. Customers and suppliers sit with the other
        // ledgers rather than under Sales and Purchase - they are opened and corrected
        // as masters, and a heading holding one entry is a click spent for nothing.
        new(
            "masters",
            "Masters",
            "البيانات الأساسية",
            "accounting",
            Children:
            [
                new(
                    "masters.inventory",
                    "Product",
                    "المنتجات",
                    "inventory",
                    Children:
                    [
                        // Inventory masters, surfaced under the shared Masters heading
                        // rather than a module of their own. The Module field records
                        // where each really belongs.
                        new(
                            "masters.products",
                            "Products",
                            "المنتجات",
                            "inventory",
                            "/inventory/products",
                            "inventory:product:view"),
                        new(
                            "masters.categories",
                            "Categories",
                            "الفئات",
                            "inventory",
                            "/inventory/categories",
                            "inventory:category:view"),
                        new(
                            "masters.brands",
                            "Brands",
                            "العلامات التجارية",
                            "inventory",
                            "/inventory/brands",
                            "inventory:category:view"),
                        new(
                            "masters.units",
                            "Units of measure",
                            "وحدات القياس",
                            "inventory",
                            "/inventory/units",
                            "inventory:unit:view"),
                        new(
                            "masters.warehouses",
                            "Warehouses",
                            "المستودعات",
                            "inventory",
                            "/inventory/warehouses",
                            "inventory:warehouse:view"),

                        // The percentages a product can carry. Part of setting up what
                        // is sold, so behind the product permission.
                        new(
                            "masters.tax-rates",
                            "Tax rates",
                            "نسب الضريبة",
                            "inventory",
                            "/inventory/tax-rates",
                            "inventory:product:view"),

                        // The stock a firm holds on the day it starts. A job rather than
                        // a document type - stated once and then never again.
                        new(
                            "masters.opening-stock",
                            "Opening stock",
                            "المخزون الافتتاحي",
                            "inventory",
                            "/inventory/opening-stock",
                            "inventory:stock-adjustment:view"),
                    ]),

                new(
                    "masters.accounts",
                    "Ledger",
                    "الحسابات",
                    "accounting",
                    Children:
                    [
                        new(
                            "masters.ledgers",
                            "Chart of accounts",
                            "دليل الحسابات",
                            "accounting",
                            "/accounting/ledgers",
                            "accounting:ledger:view"),
                        new(
                            "sales.customers",
                            "Customers",
                            "العملاء",
                            "sales",
                            "/sales/customers",
                            "sales:customer:view"),
                        new(
                            "purchase.suppliers",
                            "Suppliers",
                            "الموردون",
                            "purchase",
                            "/purchase/suppliers",
                            "purchase:supplier:view"),
                        new(
                            "masters.employees",
                            "Employees",
                            "الموظفون",
                            "accounting",
                            "/accounting/employees",
                            "accounting:ledger:view"),
                        new(
                            "masters.countries",
                            "Countries & states",
                            "الدول والمناطق",
                            "sales",
                            "/sales/countries",
                            "sales:customer:view"),
                        new(
                            "masters.territories",
                            "Areas & routes",
                            "المناطق وخطوط السير",
                            "sales",
                            "/sales/territories",
                            "sales:customer:view"),
                    ]),
            ]),

        new(
            "transactions",
            "Transactions",
            "الحركات",
            "accounting",
            Children:
            [
                // Payments and receipts lead, and each has an entry of its own.
                // They are the two commonest documents in the system and the least
                // like a journal - one party, one cash or bank account, one amount -
                // and putting them behind a voucher-type dropdown on the journal
                // screen made a cashier build a double entry to record a customer
                // handing over a note. The journal is still here for the entries
                // that really are journals.
                new(
                    "transactions.receipts",
                    "Receipt",
                    "سند قبض",
                    "accounting",
                    "/accounting/receipts/new",
                    "accounting:voucher:create"),

                new(
                    "transactions.payments",
                    "Payment",
                    "سند صرف",
                    "accounting",
                    "/accounting/payments/new",
                    "accounting:voucher:create"),

                new(
                    "transactions.voucher-entry",
                    "Voucher entry",
                    "إدخال قيد",
                    "accounting",
                    "/accounting/vouchers/new",
                    "accounting:voucher:create"),

                // Under Transactions rather than under a heading of its own. A stock
                // movement is a transaction, and somebody entering a receipt is doing
                // the same kind of work as somebody entering a voucher.
                new(
                    "transactions.stock",
                    "Stock operations",
                    "حركات المخزون",
                    "inventory",
                    "/inventory/stock",
                    "inventory:stock-adjustment:view"),

                // Invoices and credit notes on one entry, because they are one kind of
                // document: a screen showing a customer's history wants both, and two
                // entries would suggest two places to look for the same sale.
                // Before the invoices, because that is the order the work happens in:
                // an order is taken, then filled.
                new(
                    "transactions.sales-orders",
                    "Sales orders",
                    "أوامر البيع",
                    "sales",
                    "/sales/orders",
                    "sales:order:view"),

                new(
                    "transactions.sales",
                    "Sales invoices",
                    "فواتير المبيعات",
                    "sales",
                    "/sales/invoices",
                    "sales:invoice:view"),

                new(
                    "transactions.sales-returns",
                    "Sales returns",
                    "مرتجعات المبيعات",
                    "sales",
                    "/sales/returns",
                    "sales:invoice:view"),

                // Before the purchases, for the reason the sales orders come before the
                // sales invoices: an order is placed, then filled.
                new(
                    "transactions.purchase-orders",
                    "Purchase orders",
                    "أوامر الشراء",
                    "purchase",
                    "/purchase/orders",
                    "purchase:order:view"),

                // Purchases and purchase returns on two entries rather than one.
                // They are one kind of document running in opposite directions, which
                // is why one screen serves both - but somebody sending goods back is
                // doing one of the two, and a single entry meant opening a list of
                // purchases and switching a dropdown to find the returns among them.
                new(
                    "transactions.purchase",
                    "Purchases",
                    "المشتريات",
                    "purchase",
                    "/purchase/invoices",
                    "purchase:invoice:view"),

                new(
                    "transactions.purchase-returns",
                    "Purchase returns",
                    "مرتجعات المشتريات",
                    "purchase",
                    "/purchase/returns",
                    "purchase:invoice:view"),
            ]),

        new(
            "inventory-reports",
            "Stock reports",
            "تقارير المخزون",
            "inventory",
            Children:
            [
                new(
                    "inventory-reports.valuation",
                    "Stock valuation",
                    "تقييم المخزون",
                    "inventory",
                    "/inventory/valuation",
                    ViewInventoryReports),
                new(
                    "inventory-reports.ledger",
                    "Stock ledger",
                    "دفتر المخزون",
                    "inventory",
                    "/inventory/stock-ledger",
                    ViewInventoryReports),
                new(
                    "inventory-reports.movement",
                    "Item movement",
                    "حركة الأصناف",
                    "inventory",
                    "/inventory/item-movement",
                    ViewInventoryReports),
                new(
                    "inventory-reports.batch-stock",
                    "Batch-wise stock",
                    "المخزون حسب التشغيلة",
                    "inventory",
                    "/inventory/batch-stock",
                    ViewInventoryReports),
                new(
                    "inventory-reports.expiry",
                    "Expiry report",
                    "تقرير الصلاحية",
                    "inventory",
                    "/inventory/expiry",
                    ViewInventoryReports),
                new(
                    "inventory-reports.serials",
                    "Serial numbers",
                    "الأرقام التسلسلية",
                    "inventory",
                    "/inventory/serials",
                    ViewInventoryReports),
            ]),

        new(
            "accounts-reports",
            "Accounts reports",
            "تقارير الحسابات",
            "accounting",
            Children:
            [
                // First, because it is the report asked for most: what one customer,
                // supplier, bank or expense head did over a period, posting by posting.
                new(
                    "accounts-reports.ledger-statement",
                    "Ledger statement",
                    "كشف حساب",
                    "accounting",
                    "/accounting/ledger-statement",
                    ViewReports),
                new(
                    "accounts-reports.trial-balance",
                    "Trial balance",
                    "ميزان المراجعة",
                    "accounting",
                    "/accounting/trial-balance",
                    ViewReports),
                new(
                    "accounts-reports.group-summary",
                    "Group summary",
                    "ملخص المجموعات",
                    "accounting",
                    "/accounting/account-group-summary",
                    ViewReports),
                new(
                    "accounts-reports.profit-and-loss",
                    "Profit and loss",
                    "الأرباح والخسائر",
                    "accounting",
                    "/accounting/profit-and-loss",
                    ViewReports),
                new(
                    "accounts-reports.balance-sheet",
                    "Balance sheet",
                    "الميزانية العمومية",
                    "accounting",
                    "/accounting/balance-sheet",
                    ViewReports),
                new(
                    "accounts-reports.cash-flow",
                    "Cash flow",
                    "التدفقات النقدية",
                    "accounting",
                    "/accounting/cash-flow",
                    ViewReports),

                // One entry, not one per regime. Open question 1 asked for report menus
                // filtered by regime so a VAT firm is never shown a GST return; the
                // report answers in whichever heads the firm actually charges, so there
                // is nothing left to filter and no second entry to keep in step.
                new(
                    "accounts-reports.tax-returns",
                    "VAT / GST returns",
                    "الإقرارات الضريبية",
                    "accounting",
                    "/accounting/tax-returns",
                    ViewReports),
                new(
                    "accounts-reports.day-book",
                    "Day book",
                    "دفتر اليومية",
                    "accounting",
                    "/accounting/day-book",
                    ViewReports),
                new(
                    "accounts-reports.voucher-report",
                    "Voucher report",
                    "تقرير القيود",
                    "accounting",
                    "/accounting/voucher-report",
                    ViewReports),
                new(
                    "accounts-reports.transaction-summary",
                    "Transaction summary",
                    "ملخص الحركات",
                    "accounting",
                    "/accounting/transaction-summary",
                    ViewReports),
                new(
                    "accounts-reports.cash-book",
                    "Cash book",
                    "دفتر الصندوق",
                    "accounting",
                    "/accounting/cash-book",
                    ViewReports),
                new(
                    "accounts-reports.bank-book",
                    "Bank book",
                    "دفتر البنك",
                    "accounting",
                    "/accounting/bank-book",
                    ViewReports),

                // What is owed each way, bill by bill or aged. The figures were served
                // all along; nothing in the menu reached them.
                new(
                    "accounts-reports.debtors",
                    "Debtors outstanding",
                    "أرصدة العملاء المستحقة",
                    "accounting",
                    "/accounting/debtors",
                    ViewReports),
                new(
                    "accounts-reports.creditors",
                    "Creditors outstanding",
                    "أرصدة الموردين المستحقة",
                    "accounting",
                    "/accounting/creditors",
                    ViewReports),
            ]),

        new(
            "cheques",
            "Cheques",
            "الشيكات",
            "accounting",
            Children:
            [
                new(
                    "cheques.post-dated",
                    "Post-dated cheques",
                    "الشيكات المؤجلة",
                    "accounting",
                    "/accounting/post-dated-cheques",
                    ViewReports),
                new(
                    "cheques.calendar",
                    "Cheque calendar",
                    "تقويم الشيكات",
                    "accounting",
                    "/accounting/cheque-calendar",
                    ViewReports),
                new(
                    "cheques.register",
                    "Cheque register",
                    "سجل الشيكات",
                    "accounting",
                    "/accounting/cheque-register",
                    ViewReports),
            ]),

        new(
            "settings",
            "Settings",
            "الإعدادات",
            "platform",
            Children:
            [
                // The screen that edits this menu, reachable from the menu it edits.
                // Behind the menu permission rather than the reports one, so holding
                // it is a deliberate grant: rearranging what everybody else sees is
                // an administrative act, not a consequence of being able to read.
                new(
                    "settings.menu",
                    "Menu settings",
                    "إعدادات القائمة",
                    "platform",
                    "/settings/menu",
                    "platform:menu:view"),

                // Who may sign in, and what each role does and sees. Behind the
                // permissions that guard the screens' own endpoints, so the entry is
                // never offered to somebody the screen would refuse.
                new(
                    "settings.users",
                    "Users",
                    "المستخدمون",
                    "platform",
                    "/settings/users",
                    "platform:user:view"),

                new(
                    "settings.roles",
                    "Roles and menus",
                    "الأدوار والقوائم",
                    "platform",
                    "/settings/roles",
                    "platform:role:view"),

                // How each document prints - preview, layout, printer, copies, and a
                // second run - and the layouts themselves. Readable by anybody who may
                // read settings; the designer behind the print-template permission.
                new(
                    "settings.printing",
                    "Print settings",
                    "إعدادات الطباعة",
                    "platform",
                    "/settings/printing",
                    "platform:setting:view"),

                new(
                    "settings.print-templates",
                    "Print templates",
                    "قوالب الطباعة",
                    "printing",
                    "/settings/print-templates",
                    "printing:template:view"),

                // Clearing a firm's data to hand it over or to test from nothing. Behind
                // the permission to delete the firm, the nearest grant to what it does.
                new(
                    "settings.data",
                    "Clear data",
                    "مسح البيانات",
                    "platform",
                    "/settings/data",
                    "platform:firm:delete"),

                // Bringing a firm across from EasyRetail. Behind the permission to edit
                // the firm, which the import's own endpoints ask for: it writes the
                // firm's chart, parties, products, balances and stock wholesale.
                new(
                    "settings.import",
                    "Import data",
                    "استيراد البيانات",
                    "platform",
                    "/settings/import",
                    "platform:firm:edit"),

                // No permission: everything behind it is a preference about how this
                // browser behaves - the tax rates a picker offers, whether Arabic
                // names are translated, how many rows a page holds - and none of it
                // changes what a saved document means.
                new(
                    "settings.preferences",
                    "Preferences",
                    "التفضيلات",
                    "platform",
                    "/settings"),
            ]),
    ];
}
