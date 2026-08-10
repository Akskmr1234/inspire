using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChequeReversalVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reversal_voucher_id",
                table: "cheques",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cheques_reversal_voucher_id",
                table: "cheques",
                column: "reversal_voucher_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cheques_vouchers_reversal_voucher_id",
                table: "cheques",
                column: "reversal_voucher_id",
                principalTable: "vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cheques_vouchers_reversal_voucher_id",
                table: "cheques");

            migrationBuilder.DropIndex(
                name: "ix_cheques_reversal_voucher_id",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "reversal_voucher_id",
                table: "cheques");
        }
    }
}
