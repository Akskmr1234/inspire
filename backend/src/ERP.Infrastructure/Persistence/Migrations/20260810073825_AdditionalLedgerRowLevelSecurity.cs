using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends row-level security to the additional-charge matrix.
    /// </summary>
    /// <remarks>
    /// The matrix says which charges a firm puts on which documents and which way each
    /// moves a total. It is read on every invoice that is entered, which makes it a
    /// table worth pointing somewhere else if you were trying to.
    /// <para>
    /// The existing "every tenant-scoped table has forced row-level security" test
    /// fails the build if it is missed.
    /// </para>
    /// </remarks>
    public partial class AdditionalLedgerRowLevelSecurity : Migration
    {
        private const string Table = "additional_ledgers";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                ALTER TABLE public."{Table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."{Table}" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS "{Table}_tenant_isolation" ON public."{Table}";

                CREATE POLICY "{Table}_tenant_isolation" ON public."{Table}"
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql($"""
                DROP POLICY IF EXISTS "{Table}_tenant_isolation" ON public."{Table}";
                ALTER TABLE public."{Table}" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."{Table}" DISABLE ROW LEVEL SECURITY;
                """);
    }
}
