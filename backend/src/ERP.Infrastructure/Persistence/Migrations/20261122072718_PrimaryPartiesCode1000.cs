using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrimaryPartiesCode1000 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledgers_firm_code",
                table: "ledgers");

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_firm_kind_code",
                table: "ledgers",
                columns: new[] { "firm_id", "kind", "code" },
                unique: true,
                filter: "is_deleted = false");

            // The Primary customer and supplier are 1000 from now on. Existing ones are
            // renumbered unless their firm already uses 1000 for that kind of ledger.
            migrationBuilder.Sql("""
                UPDATE ledgers AS primary_party
                SET code = '1000'
                WHERE primary_party.is_primary = true
                  AND primary_party.is_deleted = false
                  AND primary_party.code IN ('PRIMARY-CUSTOMER', 'PRIMARY-SUPPLIER')
                  AND NOT EXISTS (
                      SELECT 1 FROM ledgers other
                      WHERE other.firm_id = primary_party.firm_id
                        AND other.kind = primary_party.kind
                        AND other.code = '1000'
                        AND other.is_deleted = false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the codes the old index can hold: one code per firm, of any kind.
            migrationBuilder.Sql("""
                UPDATE ledgers
                SET code = CASE kind WHEN 4 THEN 'PRIMARY-CUSTOMER' ELSE 'PRIMARY-SUPPLIER' END
                WHERE is_primary = true AND is_deleted = false AND code = '1000';
                """);

            migrationBuilder.DropIndex(
                name: "ix_ledgers_firm_kind_code",
                table: "ledgers");

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_firm_code",
                table: "ledgers",
                columns: new[] { "firm_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
