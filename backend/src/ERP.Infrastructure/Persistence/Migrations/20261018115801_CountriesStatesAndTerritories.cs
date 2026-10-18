using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the country, state, area and route masters, party photographs, and the
    /// customer details that go with them on the ledger.
    /// </summary>
    public partial class CountriesStatesAndTerritories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "area_id",
                table: "ledgers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "ledgers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_person",
                table: "ledgers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country_code",
                table: "ledgers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cst_number",
                table: "ledgers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fax",
                table: "ledgers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gst_number",
                table: "ledgers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pin_code",
                table: "ledgers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "route_id",
                table: "ledgers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "sends_sms",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tin_number",
                table: "ledgers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "ledgers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "countries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_arabic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    currency_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "party_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(700000)", maxLength: 700000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_photos_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "territories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_territories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_arabic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                    table.ForeignKey(
                        name: "fk_regions_countries_country_id",
                        column: x => x.country_id,
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_area_id",
                table: "ledgers",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_route_id",
                table: "ledgers",
                column: "route_id");

            migrationBuilder.CreateIndex(
                name: "ix_countries_code",
                table: "countries",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_countries_tenant",
                table: "countries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_party_photos_ledger",
                table: "party_photos",
                column: "ledger_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_party_photos_tenant",
                table: "party_photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_regions_code",
                table: "regions",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regions_country_id",
                table: "regions",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_regions_tenant",
                table: "regions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_territories_name",
                table: "territories",
                columns: new[] { "firm_id", "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_territories_tenant",
                table: "territories",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ledgers_territories_area_id",
                table: "ledgers",
                column: "area_id",
                principalTable: "territories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ledgers_territories_route_id",
                table: "ledgers",
                column: "route_id",
                principalTable: "territories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ledgers_territories_area_id",
                table: "ledgers");

            migrationBuilder.DropForeignKey(
                name: "fk_ledgers_territories_route_id",
                table: "ledgers");

            migrationBuilder.DropTable(
                name: "party_photos");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "territories");

            migrationBuilder.DropTable(
                name: "countries");

            migrationBuilder.DropIndex(
                name: "ix_ledgers_area_id",
                table: "ledgers");

            migrationBuilder.DropIndex(
                name: "ix_ledgers_route_id",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "area_id",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "city",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "contact_person",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "country_code",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "cst_number",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "fax",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "gst_number",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "pin_code",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "route_id",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "sends_sms",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "tin_number",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "website",
                table: "ledgers");
        }
    }
}
