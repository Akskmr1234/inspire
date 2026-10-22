using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Puts Service sales at the top of every existing firm's Service heading, and gives it
    /// to each role that was given the job cards.
    /// </summary>
    /// <remarks>
    /// A migration rather than seeding, for the reason <see cref="MenuRestructure"/> gives:
    /// a menu is a firm's own data and seeding reaches only the firm it seeds. A firm whose
    /// Service heading is gone, or that already has the entry, is left as it is. Run for
    /// each tenant in turn, because the tables are behind row-level security.
    /// </remarks>
    public partial class ServiceSalesMenu : Migration
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
                               'service.sales', 'Service sales', 'مبيعات الصيانة', NULL,
                               '/service/sales', 'service', 5, 'service:job-card:view', true,
                               true, now(), '00000000-0000-0000-0000-00000000513e'::uuid
                        FROM menu_items heading
                        WHERE heading.code = 'service'
                          AND NOT EXISTS (
                              SELECT 1 FROM menu_items existing
                              WHERE existing.firm_id = heading.firm_id
                                AND existing.code = 'service.sales');

                        INSERT INTO role_menus (role_id, menu_code, tenant_id)
                        SELECT chosen.role_id, 'service.sales', chosen.tenant_id
                        FROM role_menus chosen
                        WHERE chosen.menu_code = 'service.job-cards'
                        ON CONFLICT DO NOTHING;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        /// <remarks>Left in place: an administrator can hide it from Menu settings.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: see the remarks.
        }
    }
}
