using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChequeRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cheques",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    party_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clearing_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cheque_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    instrument_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_on = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    drawn_on_bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    deposited_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    closure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cheques", x => x.id);
                    table.ForeignKey(
                        name: "fk_cheques_ledgers_bank_ledger_id",
                        column: x => x.bank_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cheques_ledgers_party_ledger_id",
                        column: x => x.party_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cheques_vouchers_clearing_voucher_id",
                        column: x => x.clearing_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cheques_vouchers_origin_voucher_id",
                        column: x => x.origin_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cheques_bank_ledger_id",
                table: "cheques",
                column: "bank_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_by_party",
                table: "cheques",
                columns: new[] { "firm_id", "party_ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cheques_by_recorded_date",
                table: "cheques",
                columns: new[] { "firm_id", "recorded_on" });

            migrationBuilder.CreateIndex(
                name: "ix_cheques_clearing_voucher_id",
                table: "cheques",
                column: "clearing_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_issued_number",
                table: "cheques",
                columns: new[] { "firm_id", "bank_ledger_id", "cheque_number" },
                unique: true,
                filter: "direction = 2 AND status < 3");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_open_by_due_date",
                table: "cheques",
                columns: new[] { "firm_id", "instrument_date" },
                filter: "status < 3");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_origin_voucher_id",
                table: "cheques",
                column: "origin_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_party_ledger_id",
                table: "cheques",
                column: "party_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_received_number",
                table: "cheques",
                columns: new[] { "firm_id", "party_ledger_id", "cheque_number" },
                unique: true,
                filter: "direction = 1 AND status < 3");

            migrationBuilder.CreateIndex(
                name: "ix_cheques_tenant",
                table: "cheques",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cheques");
        }
    }
}
