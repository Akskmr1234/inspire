using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrimaryPartiesAlwaysCode1000 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Primary customer and supplier are 1000, whatever they were numbered before:
            // PRIMARY-CUSTOMER, the name typed as its code, anything. Tenant by tenant, as the
            // ledgers are behind row-level security - the earlier renumbering ran without a
            // tenant and so reached no rows wherever that security applies to the migrator.
            // A firm already using 1000 for another ledger of that kind keeps both as they are.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        UPDATE ledgers AS primary_party
                        SET code = '1000'
                        WHERE primary_party.is_primary = true
                          AND primary_party.is_deleted = false
                          AND primary_party.kind IN (4, 5)
                          AND primary_party.code <> '1000'
                          AND NOT EXISTS (
                              SELECT 1 FROM ledgers other
                              WHERE other.firm_id = primary_party.firm_id
                                AND other.kind = primary_party.kind
                                AND other.code = '1000'
                                AND other.is_deleted = false);
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The codes they had before are not kept; 1000 is a code the earlier schema
            // holds as well, so there is nothing to undo.
        }
    }
}
