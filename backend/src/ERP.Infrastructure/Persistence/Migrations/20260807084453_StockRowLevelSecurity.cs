using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to stock documents, positions, and the ledger.
    /// </summary>
    /// <remarks>
    /// These four tables are worth as much to a competitor as the product master
    /// beside them: what a firm holds, where it holds it, and what it paid. The
    /// ledger is worse, because its movement history shows how the business runs.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test
    /// fails the build if any of the four is missed.
    /// </para>
    /// </remarks>
    public partial class StockRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "stock_documents",
            "stock_document_lines",
            "stock_balances",
            "stock_ledger_entries",
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
