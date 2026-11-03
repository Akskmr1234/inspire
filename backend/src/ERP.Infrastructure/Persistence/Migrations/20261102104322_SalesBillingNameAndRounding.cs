using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The name a sale is made out to, how an online payment was made, and the unit its total
    /// is rounded to: empty, empty and zero for every sale entered before.
    /// </summary>
    public partial class SalesBillingNameAndRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_name",
                table: "sales_invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "online_payment_type",
                table: "sales_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "round_to",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_name",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "online_payment_type",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "round_to",
                table: "sales_invoices");
        }
    }
}
