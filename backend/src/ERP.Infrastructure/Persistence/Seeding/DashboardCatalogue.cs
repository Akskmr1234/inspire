using ERP.Application.Platform.Dashboards;
using ERP.Domain.Platform;

namespace ERP.Infrastructure.Persistence.Seeding;

/// <summary>One panel of a seeded dashboard.</summary>
/// <param name="MetricCode">The metric the server computes.</param>
/// <param name="Title">The English heading.</param>
/// <param name="TitleArabic">The Arabic heading.</param>
/// <param name="Kind">How the figure is drawn.</param>
/// <param name="Span">How many grid columns it occupies.</param>
public sealed record WidgetBlueprint(
    string MetricCode,
    string Title,
    string TitleArabic,
    WidgetKind Kind,
    int Span = 1);

/// <summary>One seeded dashboard.</summary>
/// <param name="Code">The stable code, unique within the firm.</param>
/// <param name="Name">The English name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="RoleNames">The roles it is shown to.</param>
/// <param name="Widgets">Its panels, in display order.</param>
public sealed record DashboardBlueprint(
    string Code,
    string Name,
    string NameArabic,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<WidgetBlueprint> Widgets);

/// <summary>The dashboards every new firm starts with.</summary>
/// <remarks>
/// <para>
/// One dashboard for now, and only accounting figures on it, because accounting is
/// what the system currently records. The specification's reference screen also shows
/// sales, purchases, and top-selling items; those panels arrive with the modules that
/// produce them. A dashboard promising figures nothing computes would be worse than a
/// short one.
/// </para>
/// <para>
/// Assigned to more than one role on purpose, which is the arrangement the
/// specification's own worked example describes - overlapping audiences rather than a
/// dashboard per role.
/// </para>
/// </remarks>
public static class DashboardCatalogue
{
    /// <summary>The default dashboards, in display order.</summary>
    public static IReadOnlyList<DashboardBlueprint> Default { get; } =
    [
        new(
            "accounting-overview",
            "Accounting overview",
            "نظرة عامة على الحسابات",
            ["Super Administrator", "Firm Administrator", "Accountant"],
            [
                new(
                    DashboardMetrics.Receivables,
                    "Receivables",
                    "الذمم المدينة",
                    WidgetKind.Kpi),
                new(
                    DashboardMetrics.Payables,
                    "Payables",
                    "الذمم الدائنة",
                    WidgetKind.Kpi),
                new(
                    DashboardMetrics.CashAndBank,
                    "Cash and bank",
                    "النقد والبنك",
                    WidgetKind.Kpi),
                new(
                    DashboardMetrics.PostDatedReceivable,
                    "PDC in hand",
                    "شيكات مؤجلة لدينا",
                    WidgetKind.Kpi),
                new(
                    DashboardMetrics.PostDatedPayable,
                    "PDC issued",
                    "شيكات مؤجلة صادرة",
                    WidgetKind.Kpi),
                new(
                    DashboardMetrics.MonthlyPostings,
                    "Postings by month",
                    "القيود حسب الشهر",
                    WidgetKind.Series,
                    Span: 3),
                new(
                    DashboardMetrics.TopDebtors,
                    "Largest debtors",
                    "أكبر المدينين",
                    WidgetKind.Breakdown,
                    Span: 2),
            ]),
    ];
}
