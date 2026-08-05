using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables row-level security on the accounting tables.
    /// </summary>
    /// <remarks>
    /// The same policy shape as the earlier tenancy and identity migrations. It
    /// matters most here: these four tables hold the firm's books, and a query that
    /// crossed tenants would expose one customer's financial position to another.
    /// <para>
    /// ERP.Infrastructure.Tests asserts that every table implementing ITenantScoped
    /// carries a forced policy, so omitting one of these would fail the suite rather
    /// than ship.
    /// </para>
    /// </remarks>
    public partial class AccountingRowLevelSecurity : Migration
    {
        private static readonly string[] TenantScopedTables =
        [
            "account_groups",
            "ledgers",
            "vouchers",
            "voucher_lines",
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
