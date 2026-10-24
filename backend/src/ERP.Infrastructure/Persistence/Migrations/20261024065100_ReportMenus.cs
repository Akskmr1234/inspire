using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the new reports to every existing firm's menu: sales, purchase and profit
    /// reports under a Sales &amp; purchase group, receivables and payables beside the
    /// debtors and creditors, and the stock register and product history beside the
    /// stock reports. Each is given to the roles that were given the reports beside it.
    /// </summary>
    /// <remarks>
    /// A migration rather than seeding, for the reason <see cref="MenuRestructure"/> gives:
    /// a menu is a firm's own data and seeding reaches only the firm it seeds. A firm
    /// whose heading is gone, or that already has the entry, is left as it is. Run for
    /// each tenant in turn, because the tables are behind row-level security.
    /// </remarks>
    public partial class ReportMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        -- The Sales & purchase group, first under Reports.
                        INSERT INTO menu_items (
                            id, tenant_id, firm_id, parent_id, code, label, label_arabic, icon,
                            route, module, sort_order, required_permission, is_enabled,
                            is_system, created_at_utc, created_by)
                        SELECT gen_random_uuid(), heading.tenant_id, heading.firm_id, heading.id,
                               'reports.transactions', 'Sales & purchase', 'المبيعات والمشتريات', NULL,
                               NULL, 'accounting', 5, NULL, true,
                               true, now(), '00000000-0000-0000-0000-00000000513e'::uuid
                        FROM menu_items heading
                        WHERE heading.code = 'reports'
                          AND NOT EXISTS (
                              SELECT 1 FROM menu_items existing
                              WHERE existing.firm_id = heading.firm_id
                                AND existing.code = 'reports.transactions');

                        -- Each new report, under the heading it belongs to.
                        INSERT INTO menu_items (
                            id, tenant_id, firm_id, parent_id, code, label, label_arabic, icon,
                            route, module, sort_order, required_permission, is_enabled,
                            is_system, created_at_utc, created_by)
                        SELECT gen_random_uuid(), heading.tenant_id, heading.firm_id, heading.id,
                               entry.code, entry.label, entry.label_arabic, NULL,
                               entry.route, entry.module, entry.sort_order, entry.permission, true,
                               true, now(), '00000000-0000-0000-0000-00000000513e'::uuid
                        FROM (VALUES
                            ('reports.transactions', 'reports.sales', 'Sales report', 'تقرير المبيعات',
                             '/reports/sales', 'accounting', 10, 'accounting:report:view'),
                            ('reports.transactions', 'reports.purchases', 'Purchase report', 'تقرير المشتريات',
                             '/reports/purchases', 'accounting', 20, 'accounting:report:view'),
                            ('reports.transactions', 'reports.profit', 'Profit report', 'تقرير الأرباح',
                             '/reports/profit', 'accounting', 30, 'accounting:report:view'),
                            ('accounts-reports', 'accounts-reports.receivables', 'Receivables', 'المبالغ المستحقة لنا',
                             '/accounting/receivables', 'accounting', 75, 'accounting:report:view'),
                            ('accounts-reports', 'accounts-reports.payables', 'Payables', 'المبالغ المستحقة علينا',
                             '/accounting/payables', 'accounting', 85, 'accounting:report:view'),
                            ('inventory-reports', 'inventory-reports.register', 'Stock register', 'سجل المخزون',
                             '/inventory/stock-register', 'inventory', 15, 'inventory:report:view'),
                            ('inventory-reports', 'inventory-reports.product-history', 'Product history', 'سجل المنتج',
                             '/inventory/product-history', 'inventory', 25, 'inventory:report:view'))
                            AS entry(parent_code, code, label, label_arabic, route, module, sort_order, permission)
                        JOIN menu_items heading ON heading.code = entry.parent_code
                        WHERE NOT EXISTS (
                            SELECT 1 FROM menu_items existing
                            WHERE existing.firm_id = heading.firm_id
                              AND existing.code = entry.code);

                        -- Given to the roles that were given the reports beside them.
                        INSERT INTO role_menus (role_id, menu_code, tenant_id)
                        SELECT DISTINCT chosen.role_id, grant_.code, chosen.tenant_id
                        FROM (VALUES
                            ('reports.sales', 'accounts-reports.%'),
                            ('reports.purchases', 'accounts-reports.%'),
                            ('reports.profit', 'accounts-reports.%'),
                            ('accounts-reports.receivables', 'accounts-reports.debtors'),
                            ('accounts-reports.payables', 'accounts-reports.creditors'),
                            ('inventory-reports.register', 'inventory-reports.%'),
                            ('inventory-reports.product-history', 'inventory-reports.%'))
                            AS grant_(code, beside)
                        JOIN role_menus chosen ON chosen.menu_code LIKE grant_.beside
                        ON CONFLICT DO NOTHING;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        /// <remarks>Left in place: an administrator can hide them from Menu settings.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: see the remarks.
        }
    }
}
