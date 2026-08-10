using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdditionalLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "additional_ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<int>(type: "integer", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applies_under_tax = table.Column<bool>(type: "boolean", nullable: false),
                    applies_under_cst = table.Column<bool>(type: "boolean", nullable: false),
                    applies_under_non_tax = table.Column<bool>(type: "boolean", nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_additional_ledgers", x => x.id);
                    table.ForeignKey(
                        name: "fk_additional_ledgers_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_additional_ledgers_document",
                table: "additional_ledgers",
                columns: new[] { "firm_id", "document", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_additional_ledgers_ledger_id",
                table: "additional_ledgers",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_additional_ledgers_mapping",
                table: "additional_ledgers",
                columns: new[] { "firm_id", "document", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_additional_ledgers_tenant",
                table: "additional_ledgers",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "additional_ledgers");
        }
    }
}
