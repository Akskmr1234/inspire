using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>The Primary customer and supplier a firm is given when it has none: one of each.</summary>
    public partial class LedgerPrimaryParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_primary",
                table: "ledgers",
                columns: new[] { "firm_id", "kind" },
                unique: true,
                filter: "is_primary = true AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledgers_primary",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "ledgers");
        }
    }
}
