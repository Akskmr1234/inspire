using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the cheque register.
    /// </summary>
    /// <remarks>
    /// The register holds instrument numbers, bank names, amounts, and which of a
    /// firm's customers have had a cheque bounce - commercially sensitive on its own,
    /// and exactly the sort of thing a competitor would pay for. It is covered for
    /// the same reason every other tenant-scoped table is, and the existing "every
    /// tenant-scoped table has forced row-level security" test fails the build if it
    /// is ever missed.
    /// </remarks>
    public partial class ChequeRowLevelSecurity : Migration
    {
        private const string Table = "cheques";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
            migrationBuilder.Sql($"""
                DROP POLICY IF EXISTS "{Table}_tenant_isolation" ON public."{Table}";
                ALTER TABLE public."{Table}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."{Table}" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
