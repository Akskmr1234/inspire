using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NumberingSeriesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "numbering_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    suffix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    separator = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    financial_year_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    starting_number = table.Column<int>(type: "integer", nullable: false),
                    number_length = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numbering_series", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_numbering_series_scope",
                table: "numbering_series",
                columns: new[] { "firm_id", "document_type", "branch_id", "financial_year_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_numbering_series_tenant",
                table: "numbering_series",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "numbering_series");
        }
    }
}
