using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredTransactionTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "charge_total",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_total",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_difference",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_total",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_total",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "sales_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "charge_total",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_total",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_difference",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_total",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_total",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "charge_total",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_total",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_difference",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_total",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_total",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "purchase_orders",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "charge_total",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_total",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_difference",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_total",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_total",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "purchase_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_total",
                table: "sales_order_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                computedColumnSql: "taxable_amount + tax_amount",
                stored: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_total",
                table: "sales_invoice_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                computedColumnSql: "taxable_amount + tax_amount",
                stored: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_total",
                table: "purchase_order_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                computedColumnSql: "taxable_amount + tax_amount",
                stored: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_total",
                table: "purchase_invoice_lines",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                computedColumnSql: "taxable_amount + tax_amount",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "line_total",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "line_total",
                table: "sales_invoice_lines");

            migrationBuilder.DropColumn(
                name: "line_total",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "line_total",
                table: "purchase_invoice_lines");

            migrationBuilder.DropColumn(
                name: "charge_total",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "gross_total",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "rounding_difference",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "tax_total",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "taxable_total",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "total",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "charge_total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "gross_total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "rounding_difference",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "tax_total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "taxable_total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "total",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "charge_total",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "gross_total",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "rounding_difference",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "tax_total",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "taxable_total",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "total",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "charge_total",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "gross_total",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "rounding_difference",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "tax_total",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "taxable_total",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "total",
                table: "purchase_invoices");
        }
    }
}
