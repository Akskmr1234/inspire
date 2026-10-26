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
/// Grouped by area of work - masters, sales, purchase, accounts, inventory, service,
/// reports, settings - with every report under the one Reports heading, so the
/// documents somebody enters and the reports that read them are never mixed. Only
/// screens that exist appear. A menu naming pages that are not built yet is a list of
/// complaints waiting to be filed.
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
    /// <remarks>
    /// One heading per area of work, each holding what is done there in the order it
    /// is done: masters, then what is sold, bought, paid and moved, then what is read
    /// back. Codes are stable across regroupings - roles choose menus by code - so an
    /// entry keeps its code when it moves, which is why a few still carry the prefix of
    /// a heading they no longer sit under.
    /// </remarks>
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

        // Masters in three groups: what a shop sells, who and what its money moves
        // between, and the lists the rest are filed under.
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

                        // The percentages a product can carry. Part of setting up what
                        // is sold, so behind the product permission.
                        new(
                            "masters.tax-rates",
                            "Tax rates",
                            "نسب الضريبة",
                            "inventory",
                            "/inventory/tax-rates",
                            "inventory:product:view"),
                        new(
                            "masters.warehouses",
                            "Warehouses",
                            "المستودعات",
                            "inventory",
                            "/inventory/warehouses",
                            "inventory:warehouse:view"),

                        // What goes into each finished good, read by a production run.
                        new(
                            "masters.boms",
                            "Bills of materials",
                            "قوائم المواد",
                            "inventory",
                            "/inventory/boms",
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

                // Customers and suppliers sit with the other ledgers rather than under
                // Sales and Purchase - they are opened and corrected as masters.
                new(
                    "masters.accounts",
                    "Ledger",
                    "الحسابات",
                    "accounting",
                    Children:
                    [
                        // Every ledger in one list, to open, create and correct
                        // directly. The chart below shows the same ledgers in their
                        // groups, which is where a new group is made.
                        new(
                            "masters.ledger-list",
                            "Ledgers",
                            "قائمة الحسابات",
                            "accounting",
                            "/accounting/ledger-list",
                            "accounting:ledger:view"),
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
                    ]),

                new(
                    "masters.general",
                    "General",
                    "عام",
                    "sales",
                    Children:
                    [
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

        // In the order the work happens: an offer is made, accepted, delivered,
        // invoiced, and sometimes sent back.
        new(
            "sales",
            "Sales",
            "المبيعات",
            "sales",
            Children:
            [
                new(
                    "transactions.sales-quotations",
                    "Quotations",
                    "عروض الأسعار",
                    "sales",
                    "/sales/quotations",
                    "sales:order:view"),
                new(
                    "transactions.sales-orders",
                    "Sales orders",
                    "أوامر البيع",
                    "sales",
                    "/sales/orders",
                    "sales:order:view"),

                // Goods out to a customer ahead of the invoice, which is raised from
                // the note and bills it without moving the goods again.
                new(
                    "transactions.delivery-notes",
                    "Delivery notes",
                    "إشعارات التسليم",
                    "sales",
                    "/sales/delivery-notes",
                    "inventory:stock-adjustment:view"),
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
            ]),

        new(
            "purchase",
            "Purchase",
            "المشتريات",
            "purchase",
            Children:
            [
                new(
                    "transactions.purchase-orders",
                    "Purchase orders",
                    "أوامر الشراء",
                    "purchase",
                    "/purchase/orders",
                    "purchase:order:view"),
                new(
                    "transactions.receipt-notes",
                    "Receipt notes",
                    "إشعارات الاستلام",
                    "purchase",
                    "/purchase/receipt-notes",
                    "inventory:stock-adjustment:view"),
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

        // Money in, money out, and the entries that are neither. Each opens on its
        // list, with a button for a new one; cheques follow because they are what a
        // receipt or payment by cheque leaves behind to be worked through.
        new(
            "accounts",
            "Accounts",
            "الحسابات",
            "accounting",
            Children:
            [
                new(
                    "transactions.receipts",
                    "Receipts",
                    "سندات القبض",
                    "accounting",
                    "/accounting/receipts",
                    "accounting:voucher:create"),
                new(
                    "transactions.payments",
                    "Payments",
                    "سندات الصرف",
                    "accounting",
                    "/accounting/payments",
                    "accounting:voucher:create"),
                new(
                    "transactions.voucher-entry",
                    "Journal",
                    "قيود اليومية",
                    "accounting",
                    "/accounting/journals",
                    "accounting:voucher:create"),
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
            ]),

        new(
            "inventory",
            "Inventory",
            "المخزون",
            "inventory",
            Children:
            [
                new(
                    "transactions.stock",
                    "Stock operations",
                    "حركات المخزون",
                    "inventory",
                    "/inventory/stock",
                    "inventory:stock-adjustment:view"),

                // A transfer between branches is a conversation - asked for, agreed,
                // sent, received - and the people at each step need to find the
                // requests waiting on them.
                new(
                    "transactions.stock-transfers",
                    "Stock transfers",
                    "تحويلات المخزون",
                    "inventory",
                    "/inventory/stock-transfers",
                    "inventory:stock-transfer:view"),
                new(
                    "transactions.stock-adjustment",
                    "Stock adjustment",
                    "تسوية المخزون",
                    "inventory",
                    "/inventory/stock-adjustment",
                    "inventory:stock-adjustment:view"),
                new(
                    "transactions.damaged-stock",
                    "Damaged stock",
                    "المخزون التالف",
                    "inventory",
                    "/inventory/damaged-stock",
                    "inventory:stock-adjustment:view"),
                new(
                    "transactions.production",
                    "Production",
                    "الإنتاج",
                    "inventory",
                    "/inventory/production",
                    "inventory:stock-adjustment:view"),
            ]),

        // Devices taken in for repair, worked and handed back; their parts and labour are
        // billed through an ordinary sales invoice.
        new(
            "service",
            "Service",
            "الصيانة",
            "service",
            Children:
            [
                // Taking a device in and billing it on one screen: the job, its parts and
                // charges, and what was paid. The job cards below are the same jobs, as
                // the workshop works through them.
                new(
                    "service.sales",
                    "Service sales",
                    "مبيعات الصيانة",
                    "service",
                    "/service/sales",
                    "service:job-card:view"),
                new(
                    "service.job-cards",
                    "Job cards",
                    "بطاقات العمل",
                    "service",
                    "/service/job-cards",
                    "service:job-card:view"),
                new(
                    "service.rmas",
                    "Returns (RMA)",
                    "المرتجعات (RMA)",
                    "service",
                    "/service/rmas",
                    "service:rma:view"),
            ]),

        // Every report under one heading, in three groups: the books, the statements
        // drawn from them, and stock.
        new(
            "reports",
            "Reports",
            "التقارير",
            "accounting",
            Children:
            [
                // What was sold and bought, and what was made on it, by month, day,
                // bill, item and more.
                new(
                    "reports.transactions",
                    "Sales & purchase",
                    "المبيعات والمشتريات",
                    "accounting",
                    Children:
                    [
                        new(
                            "reports.sales",
                            "Sales report",
                            "تقرير المبيعات",
                            "accounting",
                            "/reports/sales",
                            ViewReports),
                        new(
                            "reports.purchases",
                            "Purchase report",
                            "تقرير المشتريات",
                            "accounting",
                            "/reports/purchases",
                            ViewReports),
                        new(
                            "reports.profit",
                            "Profit report",
                            "تقرير الأرباح",
                            "accounting",
                            "/reports/profit",
                            ViewReports),
                    ]),

                new(
                    "accounts-reports",
                    "Accounts",
                    "تقارير الحسابات",
                    "accounting",
                    Children:
                    [
                        // First, because it is the report asked for most: what one
                        // customer, supplier, bank or expense head did over a period.
                        new(
                            "accounts-reports.ledger-statement",
                            "Ledger statement",
                            "كشف حساب",
                            "accounting",
                            "/accounting/ledger-statement",
                            ViewReports),
                        new(
                            "accounts-reports.day-book",
                            "Day book",
                            "دفتر اليومية",
                            "accounting",
                            "/accounting/day-book",
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
                            "accounts-reports.debtors",
                            "Debtors outstanding",
                            "أرصدة العملاء المستحقة",
                            "accounting",
                            "/accounting/debtors",
                            ViewReports),
                        new(
                            "accounts-reports.receivables",
                            "Receivables",
                            "المبالغ المستحقة لنا",
                            "accounting",
                            "/accounting/receivables",
                            ViewReports),
                        new(
                            "accounts-reports.creditors",
                            "Creditors outstanding",
                            "أرصدة الموردين المستحقة",
                            "accounting",
                            "/accounting/creditors",
                            ViewReports),
                        new(
                            "accounts-reports.payables",
                            "Payables",
                            "المبالغ المستحقة علينا",
                            "accounting",
                            "/accounting/payables",
                            ViewReports),
                    ]),

                new(
                    "reports.financial",
                    "Financial statements",
                    "القوائم المالية",
                    "accounting",
                    Children:
                    [
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

                        // One entry, not one per regime: the report answers in
                        // whichever heads the firm actually charges.
                        new(
                            "accounts-reports.tax-returns",
                            "VAT / GST returns",
                            "الإقرارات الضريبية",
                            "accounting",
                            "/accounting/tax-returns",
                            ViewReports),
                    ]),

                new(
                    "inventory-reports",
                    "Stock",
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
                            "inventory-reports.register",
                            "Stock register",
                            "سجل المخزون",
                            "inventory",
                            "/inventory/stock-register",
                            ViewInventoryReports),
                        new(
                            "inventory-reports.aging",
                            "Stock aging",
                            "أعمار المخزون",
                            "inventory",
                            "/inventory/stock-aging",
                            ViewInventoryReports),
                        new(
                            "inventory-reports.summary",
                            "Stock summary",
                            "ملخص المخزون",
                            "inventory",
                            "/inventory/stock-summary",
                            ViewInventoryReports),
                        new(
                            "inventory-reports.ledger",
                            "Stock ledger",
                            "دفتر المخزون",
                            "inventory",
                            "/inventory/stock-ledger",
                            ViewInventoryReports),
                        new(
                            "inventory-reports.product-history",
                            "Product history",
                            "سجل المنتج",
                            "inventory",
                            "/inventory/product-history",
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

                        // Behind the accounts' report permission, because it shows
                        // margins.
                        new(
                            "inventory-reports.batch-profit",
                            "Batch-wise profit",
                            "الربح حسب الدفعة",
                            "inventory",
                            "/inventory/batch-profit",
                            ViewReports),
                        new(
                            "inventory-reports.serials",
                            "Serial numbers",
                            "الأرقام التسلسلية",
                            "inventory",
                            "/inventory/serials",
                            ViewInventoryReports),
                    ]),
            ]),

        new(
            "settings",
            "Settings",
            "الإعدادات",
            "platform",
            Children:
            [
                // No permission: everything behind it is a preference about how this
                // firm and browser behave, and none of it changes what a saved
                // document means.
                new(
                    "settings.preferences",
                    "Preferences",
                    "التفضيلات",
                    "platform",
                    "/settings"),

                // Who may sign in, and what each role does and sees.
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

                // Changes to posted documents waiting for approval. No permission: the
                // screen shows each person the requests they may decide and those they made.
                new(
                    "settings.approvals",
                    "Approvals",
                    "الموافقات",
                    "platform",
                    "/settings/approvals"),

                // The screen that edits this menu, behind the menu permission so holding
                // it is a deliberate grant.
                new(
                    "settings.menu",
                    "Menu settings",
                    "إعدادات القائمة",
                    "platform",
                    "/settings/menu",
                    "platform:menu:view"),
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
                new(
                    "settings.audit-log",
                    "Audit log",
                    "سجل التدقيق",
                    "platform",
                    "/settings/audit-log",
                    "platform:audit-log:view"),

                // Bringing a firm across from EasyRetail, behind the permission the
                // import's own endpoints ask for.
                new(
                    "settings.import",
                    "Import data",
                    "استيراد البيانات",
                    "platform",
                    "/settings/import",
                    "platform:firm:edit"),

                // Last, and behind the permission to delete the firm: the nearest grant
                // to what it does.
                new(
                    "settings.data",
                    "Clear data",
                    "مسح البيانات",
                    "platform",
                    "/settings/data",
                    "platform:firm:delete"),
            ]),
    ];
}
