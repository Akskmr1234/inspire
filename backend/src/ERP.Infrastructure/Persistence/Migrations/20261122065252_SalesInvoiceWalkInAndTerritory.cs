using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceWalkInAndTerritory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address",
                table: "sales_invoices",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_mobile",
                table: "sales_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "area_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "billing_address",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "billing_mobile",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "route_id",
                table: "sales_invoices");
        }
    }
}
