using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceOrderLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sales_order_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_order",
                table: "sales_invoices",
                column: "sales_order_id",
                filter: "sales_order_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_sales_orders_sales_order_id",
                table: "sales_invoices",
                column: "sales_order_id",
                principalTable: "sales_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_sales_orders_sales_order_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_order",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "sales_order_id",
                table: "sales_invoices");
        }
    }
}
