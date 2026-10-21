using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Auditing.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_module",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "module", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_record",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_user",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "user_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_when",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            // Written once, never changed. The trigger refuses an UPDATE, a DELETE or a
            // TRUNCATE from anybody, the application's own role included, so nothing short
            // of a database administrator dropping the trigger can rewrite what happened.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.refuse_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'The audit trail cannot be changed or deleted.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER audit_entries_append_only
                    BEFORE UPDATE OR DELETE ON audit.audit_entries
                    FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();

                CREATE TRIGGER audit_entries_no_truncate
                    BEFORE TRUNCATE ON audit.audit_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_change();
                """);

            // One tenant's trail is invisible to another, as their books are.
            migrationBuilder.Sql("""
                ALTER TABLE audit.audit_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit.audit_entries FORCE ROW LEVEL SECURITY;

                CREATE POLICY audit_entries_tenant_isolation ON audit.audit_entries
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS audit_entries_tenant_isolation ON audit.audit_entries;
                DROP TRIGGER IF EXISTS audit_entries_no_truncate ON audit.audit_entries;
                DROP TRIGGER IF EXISTS audit_entries_append_only ON audit.audit_entries;
                DROP FUNCTION IF EXISTS audit.refuse_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
