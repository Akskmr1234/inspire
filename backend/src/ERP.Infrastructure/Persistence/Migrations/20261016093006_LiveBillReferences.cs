using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveBillReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bills_party_reference",
                table: "bills");

            migrationBuilder.CreateIndex(
                name: "ix_bills_party_reference",
                table: "bills",
                columns: new[] { "firm_id", "ledger_id", "bill_number" },
                unique: true,
                filter: "status <> 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bills_party_reference",
                table: "bills");

            migrationBuilder.CreateIndex(
                name: "ix_bills_party_reference",
                table: "bills",
                columns: new[] { "firm_id", "ledger_id", "bill_number" },
                unique: true);
        }
    }
}
