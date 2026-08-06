using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to saved grid layouts.
    /// </summary>
    /// <remarks>
    /// A column arrangement looks like the least sensitive thing in the database, and
    /// the policy is not really there to protect it. It is there because the rule is
    /// "every tenant-scoped table", and a rule with judgement calls in it is one
    /// somebody eventually gets wrong on a table that does matter. The schema test
    /// fails the build if this is missed.
    /// </remarks>
    public partial class GridLayoutRowLevelSecurity : Migration
    {
        private const string Table = "grid_layouts";

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
