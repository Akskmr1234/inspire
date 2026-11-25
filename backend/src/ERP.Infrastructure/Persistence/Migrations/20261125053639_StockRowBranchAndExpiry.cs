using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockRowBranchAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                table: "stock_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_on",
                table: "stock_ledger_entries",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_branch",
                table: "stock_ledger_entries",
                column: "branch_id");

            // Every new movement is completed by the database: the warehouse's branch where
            // it has one (else the branch the movement was made in, which the application
            // supplies), and the batch's expiry date. A batch whose expiry is corrected
            // carries the correction to its movements.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION stock_ledger_entries_complete() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    godown_branch uuid;
                BEGIN
                    SELECT w.branch_id INTO godown_branch FROM warehouses w WHERE w.id = NEW.warehouse_id;
                    NEW.branch_id := COALESCE(godown_branch, NEW.branch_id);

                    IF NEW.batch_id IS NOT NULL AND NEW.expires_on IS NULL THEN
                        SELECT b.expires_on INTO NEW.expires_on FROM batches b WHERE b.id = NEW.batch_id;
                    END IF;

                    RETURN NEW;
                END $$;

                CREATE TRIGGER stock_ledger_entries_complete
                    BEFORE INSERT ON stock_ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION stock_ledger_entries_complete();

                CREATE OR REPLACE FUNCTION batches_expiry_to_stock() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    UPDATE stock_ledger_entries SET expires_on = NEW.expires_on WHERE batch_id = NEW.id;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER batches_expiry_to_stock
                    AFTER UPDATE OF expires_on ON batches
                    FOR EACH ROW WHEN (OLD.expires_on IS DISTINCT FROM NEW.expires_on)
                    EXECUTE FUNCTION batches_expiry_to_stock();
                """);

            // The movements already recorded, tenant by tenant (the tables are behind
            // row-level security): the warehouse's branch, else the branch of the sale or
            // purchase that moved the goods, else the firm's branch where it has only one.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        UPDATE stock_ledger_entries e SET branch_id = w.branch_id
                        FROM warehouses w
                        WHERE w.id = e.warehouse_id AND w.branch_id IS NOT NULL AND e.branch_id IS NULL;

                        UPDATE stock_ledger_entries e SET branch_id = s.branch_id
                        FROM sales_invoices s
                        WHERE s.stock_document_id = e.document_id AND e.branch_id IS NULL;

                        UPDATE stock_ledger_entries e SET branch_id = p.branch_id
                        FROM purchase_invoices p
                        WHERE p.stock_document_id = e.document_id AND e.branch_id IS NULL;

                        UPDATE stock_ledger_entries e SET branch_id = only_branch.id
                        FROM (
                            SELECT b.firm_id, min(b.id::text)::uuid AS id
                            FROM branches b
                            WHERE NOT b.is_deleted
                            GROUP BY b.firm_id
                            HAVING count(*) = 1
                        ) only_branch
                        WHERE only_branch.firm_id = e.firm_id AND e.branch_id IS NULL;

                        UPDATE stock_ledger_entries e SET expires_on = b.expires_on
                        FROM batches b
                        WHERE b.id = e.batch_id AND e.expires_on IS NULL AND b.expires_on IS NOT NULL;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS batches_expiry_to_stock ON batches;
                DROP FUNCTION IF EXISTS batches_expiry_to_stock();
                DROP TRIGGER IF EXISTS stock_ledger_entries_complete ON stock_ledger_entries;
                DROP FUNCTION IF EXISTS stock_ledger_entries_complete();
                """);

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entries_branch",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "expires_on",
                table: "stock_ledger_entries");
        }
    }
}
