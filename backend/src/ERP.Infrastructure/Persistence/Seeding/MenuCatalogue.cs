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

        new(
            "masters",
            "Masters",
            "البيانات الأساسية",
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

                // Inventory masters, surfaced under the shared Masters heading rather
                // than a module of their own. The specification asks for exactly this -
                // an entry belonging to one module shown under another - and the
                // Module field records where each really belongs.
                new(
                    "masters.products",
                    "Products",
                    "المنتجات",
                    "inventory",
                    "/inventory/products",
                    "inventory:product:view"),
                new(
                    "masters.units",
                    "Units of measure",
                    "وحدات القياس",
                    "inventory",
                    "/inventory/units",
                    "inventory:unit:view"),
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
                    "masters.warehouses",
                    "Warehouses",
                    "المستودعات",
                    "inventory",
                    "/inventory/warehouses",
                    "inventory:warehouse:view"),
            ]),

        new(
            "transactions",
            "Transactions",
            "الحركات",
            "accounting",
            Children:
            [
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

                // Purchases and debit notes on one entry, for the reason sales are on one:
                // they are one kind of document, and a supplier's history wants both.
                new(
                    "transactions.purchase",
                    "Purchases",
                    "المشتريات",
                    "purchase",
                    "/purchase/invoices",
                    "purchase:invoice:view"),
            ]),

        new(
            "sales",
            "Sales",
            "المبيعات",
            "sales",
            Children:
            [
                // The customer master sits under Sales rather than among the accounting
                // masters, even though a customer is a sub-ledger. It is where somebody
                // selling looks for it, and the menu follows the work rather than the
                // storage.
                new(
                    "sales.customers",
                    "Customers",
                    "العملاء",
                    "sales",
                    "/sales/customers",
                    "sales:customer:view"),
            ]),

        new(
            "purchase",
            "Purchase",
            "المشتريات",
            "purchase",
            Children:
            [
                // Beside the customer master and for the same reason: a supplier is a
                // sub-ledger, but this is where somebody buying looks for it.
                new(
                    "purchase.suppliers",
                    "Suppliers",
                    "الموردون",
                    "purchase",
                    "/purchase/suppliers",
                    "purchase:supplier:view"),
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
            ]),

        new(
            "accounts-reports",
            "Accounts reports",
            "تقارير الحسابات",
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
            ]),
    ];
}
