using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the product master and its barcodes.
    /// </summary>
    /// <remarks>
    /// The product master is a firm's entire range with its cost prices against it -
    /// what it buys, from whom, at what margin. It is the single most valuable table
    /// in the schema to a competitor, and the barcodes beside it carry the same cost
    /// and retail figures per pack size.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test
    /// fails the build if either is missed.
    /// </para>
    /// </remarks>
    public partial class ProductRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "products",
            "product_barcodes",
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
