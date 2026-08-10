using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the sales invoice and everything hanging off it.
    /// </summary>
    /// <remarks>
    /// An invoice names a firm's customers, what they bought, what they paid, and what
    /// tax was charged on it. Of everything in this schema it is the closest thing to
    /// the business itself, and the tax rows beside it are what a return is built from.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test
    /// fails the build if any of the five is missed.
    /// </para>
    /// </remarks>
    public partial class SalesRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "sales_invoices",
            "sales_invoice_lines",
            "sales_invoice_line_serials",
            "sales_invoice_line_taxes",
            "sales_invoice_charges",
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
