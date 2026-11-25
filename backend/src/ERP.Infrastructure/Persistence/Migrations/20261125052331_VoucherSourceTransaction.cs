using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoucherSourceTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_number",
                table: "vouchers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "vouchers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_source",
                table: "vouchers",
                column: "source_id");

            // Vouchers already posted are linked from the documents that name them, tenant
            // by tenant, because the tables are behind row-level security. Sales and
            // purchases first; a stock document or a cheque only where nothing has claimed
            // the voucher already.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        UPDATE vouchers v
                        SET source_type = CASE s.kind WHEN 2 THEN 'SalesReturn' ELSE 'Sales' END,
                            source_id = s.id, source_number = left(s.number, 60)
                        FROM sales_invoices s
                        WHERE v.id IN (s.journal_voucher_id, s.receipt_voucher_id) AND v.source_id IS NULL;

                        UPDATE vouchers v
                        SET source_type = CASE p.kind WHEN 2 THEN 'PurchaseReturn' ELSE 'Purchase' END,
                            source_id = p.id, source_number = left(p.number, 60)
                        FROM purchase_invoices p
                        WHERE v.id = p.journal_voucher_id AND v.source_id IS NULL;

                        UPDATE vouchers v
                        SET source_type = CASE d.type
                                WHEN 1 THEN 'OpeningStock' WHEN 2 THEN 'MaterialReceipt'
                                WHEN 3 THEN 'MaterialIssue' WHEN 4 THEN 'StockTransfer'
                                WHEN 5 THEN 'StockAdjustment' WHEN 6 THEN 'DamagedStock'
                                WHEN 7 THEN 'PhysicalVerification' WHEN 8 THEN 'SalesIssue'
                                WHEN 9 THEN 'SalesReturn' WHEN 10 THEN 'PurchaseReceipt'
                                WHEN 11 THEN 'PurchaseReturn' WHEN 12 THEN 'DeliveryNote'
                                WHEN 13 THEN 'ReceiptNote' WHEN 14 THEN 'Production'
                                ELSE 'StockDocument' END,
                            source_id = d.id, source_number = left(d.number, 60)
                        FROM stock_documents d
                        WHERE v.id = d.journal_voucher_id AND v.source_id IS NULL;

                        UPDATE vouchers v
                        SET source_type = 'ChequeClearing', source_id = c.id, source_number = left(c.cheque_number, 60)
                        FROM cheques c
                        WHERE v.id = c.clearing_voucher_id AND v.source_id IS NULL;

                        UPDATE vouchers v
                        SET source_type = 'ChequeReversal', source_id = c.id, source_number = left(c.cheque_number, 60)
                        FROM cheques c
                        WHERE v.id = c.reversal_voucher_id AND v.source_id IS NULL;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vouchers_source",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "source_number",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "vouchers");
        }
    }
}
