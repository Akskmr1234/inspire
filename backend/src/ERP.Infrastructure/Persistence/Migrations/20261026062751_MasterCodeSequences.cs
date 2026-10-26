using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The table that remembers where each master's automatic codes have got to - 10000,
    /// 10001 and on - per firm.
    /// </summary>
    public partial class MasterCodeSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "master_code_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    master = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_issued = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_master_code_sequences", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_master_code_sequences_master",
                table: "master_code_sequences",
                columns: new[] { "firm_id", "master" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_master_code_sequences_tenant",
                table: "master_code_sequences",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "master_code_sequences");
        }
    }
}
