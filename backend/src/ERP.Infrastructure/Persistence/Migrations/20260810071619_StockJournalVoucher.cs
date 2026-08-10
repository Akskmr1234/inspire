using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockJournalVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "journal_voucher_id",
                table: "stock_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_journal_voucher_id",
                table: "stock_documents",
                column: "journal_voucher_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_documents_vouchers_journal_voucher_id",
                table: "stock_documents",
                column: "journal_voucher_id",
                principalTable: "vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_documents_vouchers_journal_voucher_id",
                table: "stock_documents");

            migrationBuilder.DropIndex(
                name: "ix_stock_documents_journal_voucher_id",
                table: "stock_documents");

            migrationBuilder.DropColumn(
                name: "journal_voucher_id",
                table: "stock_documents");
        }
    }
}
