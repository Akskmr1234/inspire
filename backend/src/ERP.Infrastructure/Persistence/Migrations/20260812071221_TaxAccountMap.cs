using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxAccountMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_account_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_account_maps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_account_assignments",
                columns: table => new
                {
                    tax_account_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_account_assignments", x => new { x.tax_account_map_id, x.component, x.direction });
                    table.ForeignKey(
                        name: "fk_tax_account_assignments_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_account_assignments_tax_account_maps_tax_account_map_id",
                        column: x => x.tax_account_map_id,
                        principalTable: "tax_account_maps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_account_assignments_ledger",
                table: "tax_account_assignments",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_account_assignments_tenant",
                table: "tax_account_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_account_maps_firm",
                table: "tax_account_maps",
                column: "firm_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_account_maps_tenant",
                table: "tax_account_maps",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_account_assignments");

            migrationBuilder.DropTable(
                name: "tax_account_maps");
        }
    }
}
