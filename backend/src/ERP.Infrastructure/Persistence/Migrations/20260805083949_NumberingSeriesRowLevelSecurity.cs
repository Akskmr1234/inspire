using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>Enables row-level security on the numbering series table.</summary>
    public partial class NumberingSeriesRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                ALTER TABLE public."numbering_series" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public."numbering_series" FORCE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS "numbering_series_tenant_isolation"
                    ON public."numbering_series";

                CREATE POLICY "numbering_series_tenant_isolation" ON public."numbering_series"
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS "numbering_series_tenant_isolation"
                    ON public."numbering_series";
                ALTER TABLE public."numbering_series" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."numbering_series" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
