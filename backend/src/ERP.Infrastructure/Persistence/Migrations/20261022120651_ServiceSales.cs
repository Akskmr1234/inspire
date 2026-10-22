using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "mode",
                table: "job_cards",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_card_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_card_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_card_charges_job_cards_job_card_id",
                        column: x => x.job_card_id,
                        principalTable: "job_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_card_charges_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_card_charges_card",
                table: "job_card_charges",
                columns: new[] { "job_card_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_job_card_charges_ledger",
                table: "job_card_charges",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_card_charges_tenant",
                table: "job_card_charges",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_card_charges");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "job_cards");
        }
    }
}
