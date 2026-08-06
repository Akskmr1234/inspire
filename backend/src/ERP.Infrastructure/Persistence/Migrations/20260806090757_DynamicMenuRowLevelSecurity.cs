using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the navigation menu.
    /// </summary>
    /// <remarks>
    /// The menu is configuration rather than trading data, which makes it tempting to
    /// treat as harmless. It is not: a firm's menu tree names the modules it runs, the
    /// screens it has been given, and - through the permission each entry requires -
    /// the shape of how it separates duties. That is a useful map for anyone who
    /// should not have it, and it is covered for the same reason every other
    /// tenant-scoped table is. The "every tenant-scoped table has forced row-level
    /// security" test fails the build if it is ever missed.
    /// </remarks>
    public partial class DynamicMenuRowLevelSecurity : Migration
    {
        private const string Table = "menu_items";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql($"""
                ALTER TABLE public."{Table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."{Table}" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS "{Table}_tenant_isolation" ON public."{Table}";

                CREATE POLICY "{Table}_tenant_isolation" ON public."{Table}"
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql($"""
                DROP POLICY IF EXISTS "{Table}_tenant_isolation" ON public."{Table}";
                ALTER TABLE public."{Table}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."{Table}" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
