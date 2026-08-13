using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the purchase invoice and everything hanging off it.
    /// </summary>
    /// <remarks>
    /// A purchase names a firm's suppliers, what it pays them, and what it is reclaiming
    /// from a tax authority. It is what a competitor would read first.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test fails
    /// the build if any of the five is missed.
    /// </para>
    /// </remarks>
    public partial class PurchaseRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "purchase_invoices",
            "purchase_invoice_lines",
            "purchase_invoice_line_serials",
            "purchase_invoice_line_taxes",
            "purchase_invoice_charges",
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
