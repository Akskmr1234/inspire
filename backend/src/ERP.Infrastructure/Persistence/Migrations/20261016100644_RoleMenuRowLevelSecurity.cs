using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Confines <c>role_menus</c> to the tenant the connection is acting for.
    /// </summary>
    /// <remarks>
    /// Separate from the table's own migration so the schema change and the policy can
    /// be read, reviewed and reverted on their own, which is how every other table here
    /// is done.
    /// </remarks>
    public partial class RoleMenuRowLevelSecurity : Migration
    {
        private const string Table = "role_menus";

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
