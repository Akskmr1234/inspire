using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Marks a firm's Primary category and Primary brand - the defaults new products are
    /// filed under, kept off the master lists and through a data reset. At most one each
    /// per firm.
    /// </summary>
    public partial class PrimaryMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "brands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_categories_primary",
                table: "categories",
                column: "firm_id",
                unique: true,
                filter: "is_primary = true");

            migrationBuilder.CreateIndex(
                name: "ix_brands_primary",
                table: "brands",
                column: "firm_id",
                unique: true,
                filter: "is_primary = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_categories_primary",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ix_brands_primary",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "brands");
        }
    }
}
