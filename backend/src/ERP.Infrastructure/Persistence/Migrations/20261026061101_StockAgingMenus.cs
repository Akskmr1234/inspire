using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the stock aging report and the stock summary beside the stock register in
    /// every existing firm's menu, given to the roles that have the stock reports.
    /// </summary>
    /// <remarks>
    /// The stock register became the on-hand report - product, batch, godown and branch,
    /// valued - and the opening-to-closing movement report it was moved to the stock
    /// summary, so neither is lost. A migration rather than seeding, as
    /// <see cref="ReportMenus"/> explains; run for each tenant in turn, because the
    /// tables are behind row-level security. A firm that already has an entry, or whose
    /// heading is gone, is left as it is.
    /// </remarks>
    public partial class StockAgingMenus : Migration
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

                        INSERT INTO menu_items (
                            id, tenant_id, firm_id, parent_id, code, label, label_arabic, icon,
                            route, module, sort_order, required_permission, is_enabled,
                            is_system, created_at_utc, created_by)
                        SELECT gen_random_uuid(), heading.tenant_id, heading.firm_id, heading.id,
                               entry.code, entry.label, entry.label_arabic, NULL,
                               entry.route, 'inventory', entry.sort_order, 'inventory:report:view', true,
                               true, now(), '00000000-0000-0000-0000-00000000513e'::uuid
                        FROM (VALUES
                            ('inventory-reports.aging', 'Stock aging', 'أعمار المخزون',
                             '/inventory/stock-aging', 16),
                            ('inventory-reports.summary', 'Stock summary', 'ملخص المخزون',
                             '/inventory/stock-summary', 17))
                            AS entry(code, label, label_arabic, route, sort_order)
                        JOIN menu_items heading ON heading.code = 'inventory-reports'
                        WHERE NOT EXISTS (
                            SELECT 1 FROM menu_items existing
                            WHERE existing.firm_id = heading.firm_id
                              AND existing.code = entry.code);

                        INSERT INTO role_menus (role_id, menu_code, tenant_id)
                        SELECT DISTINCT chosen.role_id, entry.code, chosen.tenant_id
                        FROM (VALUES ('inventory-reports.aging'), ('inventory-reports.summary'))
                            AS entry(code)
                        JOIN role_menus chosen ON chosen.menu_code LIKE 'inventory-reports.%'
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
