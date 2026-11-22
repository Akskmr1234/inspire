using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PointOfSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pos_day_closes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sales_count = table.Column<int>(type: "integer", nullable: false),
                    returns_count = table.Column<int>(type: "integer", nullable: false),
                    sales_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    returns_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cash_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    card_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    online_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_day_closes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pos_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    counted_cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_pos_shifts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pos_day_closes_firm_date",
                table: "pos_day_closes",
                columns: new[] { "firm_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pos_shifts_firm_date",
                table: "pos_shifts",
                columns: new[] { "firm_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_shifts_firm_user",
                table: "pos_shifts",
                columns: new[] { "firm_id", "user_id", "closed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_shifts_one_open",
                table: "pos_shifts",
                columns: new[] { "firm_id", "user_id" },
                unique: true,
                filter: "closed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pos_shifts_user_id",
                table: "pos_shifts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pos_day_closes");

            migrationBuilder.DropTable(
                name: "pos_shifts");
        }
    }
}
