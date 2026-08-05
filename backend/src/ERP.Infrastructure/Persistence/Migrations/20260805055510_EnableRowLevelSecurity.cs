using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables PostgreSQL row-level security on every tenant-scoped table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second of the two tenant-isolation layers. The EF Core global
    /// query filter covers ordinary LINQ; this covers everything else - raw SQL
    /// from the report builder, ExecuteUpdate and ExecuteDelete, a stray
    /// IgnoreQueryFilters, a background job, or a person with psql.
    /// </para>
    /// <para>
    /// The SQL is written out explicitly rather than generated from the model at
    /// run time. A migration is a historical record of one schema change and must
    /// keep producing the same result for ever; deriving it from a model that
    /// keeps evolving would make old migrations change meaning underneath us.
    /// RowLevelSecurity generates the equivalent statements, and the integration
    /// tests assert that every tenant-scoped table in the model is actually
    /// covered - so a table added later without a policy fails the suite rather
    /// than leaking quietly.
    /// </para>
    /// </remarks>
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <summary>The tables carrying a tenant discriminator at this revision.</summary>
        private static readonly string[] TenantScopedTables =
        [
            "firms",
            "branches",
            "financial_years",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (string table in TenantScopedTables)
            {
                // FORCE is what makes this meaningful. Without it the table owner
                // bypasses the policy entirely, and migrations, maintenance
                // scripts, and many local setups connect as the owner.
                //
                // current_setting(..., true) returns NULL instead of raising when
                // the variable is unset, and comparing against NULL matches no
                // rows - so a connection that has not declared a tenant sees
                // nothing rather than everything.
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
