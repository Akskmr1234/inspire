using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the tenant-scoped identity tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the tables that decide who may do what, so they need the same
    /// isolation as the financial data they protect. A join table left uncovered
    /// would be the one place in the database where a tenant's role assignments
    /// were readable by everybody.
    /// </para>
    /// <para>
    /// <c>permissions</c> is deliberately absent. It is the catalogue of actions
    /// the software supports, which is identical for every customer and carries no
    /// tenant column; policies on it would have nothing to compare against.
    /// Assignment - which is the tenant-specific part - lives in
    /// <c>role_permissions</c>, which is covered.
    /// </para>
    /// </remarks>
    public partial class IdentityRowLevelSecurity : Migration
    {
        /// <summary>The identity tables carrying a tenant discriminator.</summary>
        private static readonly string[] TenantScopedTables =
        [
            "roles",
            "role_permissions",
            "users",
            "user_roles",
            "user_firm_access",
            "refresh_tokens",
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
