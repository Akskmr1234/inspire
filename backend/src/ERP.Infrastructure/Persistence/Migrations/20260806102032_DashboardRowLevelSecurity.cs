using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to dashboards, their panels, and their assignments.
    /// </summary>
    /// <remarks>
    /// All three tables, not just the parent. The panels name which figures a firm
    /// watches and the assignments name who is meant to watch them - together they
    /// describe how a business is run and how it separates duties, which is a good deal
    /// more revealing than the dashboard's title. The schema test fails the build if
    /// any tenant-scoped table is left uncovered.
    /// </remarks>
    public partial class DashboardRowLevelSecurity : Migration
    {
        private static readonly string[] Tables =
        [
            "dashboards",
            "dashboard_widgets",
            "dashboard_roles",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            foreach (string table in Tables)
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
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            foreach (string table in Tables)
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
