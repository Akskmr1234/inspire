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
    /// <summary>The permission every report screen requires.</summary>
    private const string ViewReports = "accounting:report:view";

    /// <summary>The default menu tree, in display order.</summary>
    public static IReadOnlyList<MenuBlueprint> Default { get; } =
    [
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
