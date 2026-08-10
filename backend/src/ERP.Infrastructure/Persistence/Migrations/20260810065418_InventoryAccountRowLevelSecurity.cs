using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the inventory account map.
    /// </summary>
    /// <remarks>
    /// The map says which accounts a firm's stock posts to, which is a reading of their
    /// chart and of how they run their books. It is also the table a posting reads on
    /// every stock document, so it is exactly the table an attacker would want to point
    /// somewhere else.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test
    /// fails the build if either table is missed.
    /// </para>
    /// </remarks>
    public partial class InventoryAccountRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "inventory_account_maps",
            "inventory_account_assignments",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (string table in TenantScopedTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS "{table}_tenant_isolation" ON public."{table}";

                    CREATE POLICY "{table}_tenant_isolation" ON public."{table}"
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in TenantScopedTables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS "{table}_tenant_isolation" ON public."{table}";
                    ALTER TABLE public."{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE public."{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
