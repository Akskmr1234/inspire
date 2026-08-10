using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryAccountMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_account_maps",
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
                    table.PrimaryKey("pk_inventory_account_maps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_account_assignments",
                columns: table => new
                {
                    inventory_account_map_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_account_assignments", x => new { x.inventory_account_map_id, x.account });
                    table.ForeignKey(
                        name: "fk_inventory_account_assignments_inventory_account_maps_invent~",
                        column: x => x.inventory_account_map_id,
                        principalTable: "inventory_account_maps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_account_assignments_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_account_assignments_ledger",
                table: "inventory_account_assignments",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_account_assignments_tenant",
                table: "inventory_account_assignments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_account_maps_firm",
                table: "inventory_account_maps",
                column: "firm_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_account_maps_tenant",
                table: "inventory_account_maps",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_account_assignments");

            migrationBuilder.DropTable(
                name: "inventory_account_maps");
        }
    }
}
