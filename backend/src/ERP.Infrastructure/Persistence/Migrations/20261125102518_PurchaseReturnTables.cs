using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseReturnTables : Migration
    {
        private static readonly string[] ReturnTables =
        [
            "purchase_returns", "purchase_return_lines", "purchase_return_line_taxes", "purchase_return_line_serials", "purchase_return_charges",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The existing tables' keys are unchanged; they take the names EF Core gives keys
            // kept on more than one table, so the model and the database agree.
            migrationBuilder.Sql("""
                ALTER TABLE purchase_invoice_charges RENAME CONSTRAINT "fk_purchase_invoice_charges_ledgers_ledger_id" TO "FK_purchase_invoice_charges_ledgers_ledger_id";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "fk_purchase_invoice_lines_products_product_id" TO "FK_purchase_invoice_lines_products_product_id";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "fk_purchase_invoice_lines_units_of_measure_unit_id" TO "FK_purchase_invoice_lines_units_of_measure_unit_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_bills_bill_id" TO "FK_purchase_invoices_bills_bill_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_financial_years_financial_year_id" TO "FK_purchase_invoices_financial_years_financial_year_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_ledgers_supplier_ledger_id" TO "FK_purchase_invoices_ledgers_supplier_ledger_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_stock_documents_receipt_note_id" TO "FK_purchase_invoices_stock_documents_receipt_note_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_stock_documents_stock_document_id" TO "FK_purchase_invoices_stock_documents_stock_document_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_vouchers_journal_voucher_id" TO "FK_purchase_invoices_vouchers_journal_voucher_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "fk_purchase_invoices_warehouses_warehouse_id" TO "FK_purchase_invoices_warehouses_warehouse_id";
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_purchase_invoices_purchase_invoices_returns_invoice_id",
                table: "purchase_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_rmas_purchase_invoices_debit_note_id",
                table: "rmas");

            // Likewise their primary keys: renamed, not rebuilt - other tables' keys depend on them.
            migrationBuilder.Sql("""
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "pk_purchase_invoices" TO "PK_purchase_invoices";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "pk_purchase_invoice_lines" TO "PK_purchase_invoice_lines";
                ALTER TABLE purchase_invoice_line_taxes RENAME CONSTRAINT "pk_purchase_invoice_line_taxes" TO "PK_purchase_invoice_line_taxes";
                ALTER TABLE purchase_invoice_line_serials RENAME CONSTRAINT "pk_purchase_invoice_line_serials" TO "PK_purchase_invoice_line_serials";
                ALTER TABLE purchase_invoice_charges RENAME CONSTRAINT "pk_purchase_invoice_charges" TO "PK_purchase_invoice_charges";
                """);

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_warehouse_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_supplier_ledger_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_supplier_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_stock_document_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_stock_document_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_journal_voucher_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_journal_voucher_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_is_deleted",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_is_deleted");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_financial_year_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_financial_year_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_bill_id",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_bill_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_tenant",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_firm_id_supplier_ledger_id_supplier_invoi~");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_supplier",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_firm_id_supplier_ledger_id_date");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_returns_invoice",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_returns_invoice_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_receipt_note",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_receipt_note_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_number",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_firm_id_number");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoices_date",
                table: "purchase_invoices",
                newName: "IX_purchase_invoices_firm_id_date");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_lines_unit_id",
                table: "purchase_invoice_lines",
                newName: "IX_purchase_invoice_lines_unit_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_lines_tenant",
                table: "purchase_invoice_lines",
                newName: "IX_purchase_invoice_lines_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_lines_product",
                table: "purchase_invoice_lines",
                newName: "IX_purchase_invoice_lines_product_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_lines_invoice",
                table: "purchase_invoice_lines",
                newName: "IX_purchase_invoice_lines_purchase_invoice_id_line_number");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_line_taxes_component",
                table: "purchase_invoice_line_taxes",
                newName: "IX_purchase_invoice_line_taxes_tenant_id_type");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_line_serials_serial",
                table: "purchase_invoice_line_serials",
                newName: "IX_purchase_invoice_line_serials_tenant_id_serial_number");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_charges_ledger_id",
                table: "purchase_invoice_charges",
                newName: "IX_purchase_invoice_charges_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_charges_tenant",
                table: "purchase_invoice_charges",
                newName: "IX_purchase_invoice_charges_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_purchase_invoice_charges_ledger",
                table: "purchase_invoice_charges",
                newName: "IX_purchase_invoice_charges_purchase_invoice_id_ledger_id");

            migrationBuilder.CreateTable(
                name: "purchase_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    returns_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    supplier_invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    charge_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    gross_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    rounding_difference = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    tax_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    taxable_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_returns", x => x.id);
                    table.CheckConstraint("ck_purchase_returns_kind", "kind = 2");
                    table.ForeignKey(
                        name: "FK_purchase_returns_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_ledgers_supplier_ledger_id",
                        column: x => x.supplier_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_stock_documents_receipt_note_id",
                        column: x => x.receipt_note_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_vouchers_journal_voucher_id",
                        column: x => x.journal_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_returns_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_returns_purchase_invoices_returns_invoice_id",
                        column: x => x.returns_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_return_charges", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_return_charges_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_return_charges_purchase_returns_purchase_invoice_id",
                        column: x => x.purchase_return_id,
                        principalTable: "purchase_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    selling_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, computedColumnSql: "taxable_amount + tax_amount", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_return_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_return_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchase_return_lines_units_of_measure_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_return_lines_purchase_returns_purchase_invoice_id",
                        column: x => x.purchase_return_id,
                        principalTable: "purchase_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_line_serials",
                columns: table => new
                {
                    purchase_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_return_line_serials", x => new { x.purchase_return_line_id, x.serial_number });
                    table.ForeignKey(
                        name: "fk_purchase_return_line_serials_purchase_return_lines_purchase~",
                        column: x => x.purchase_return_line_id,
                        principalTable: "purchase_return_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_return_line_taxes",
                columns: table => new
                {
                    purchase_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_return_line_taxes", x => new { x.purchase_return_line_id, x.type });
                    table.ForeignKey(
                        name: "fk_purchase_return_line_taxes_purchase_return_lines_purchase_i~",
                        column: x => x.purchase_return_line_id,
                        principalTable: "purchase_return_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_charges_ledger_id",
                table: "purchase_return_charges",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_charges_purchase_return_id_ledger_id",
                table: "purchase_return_charges",
                columns: new[] { "purchase_return_id", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_charges_tenant_id",
                table: "purchase_return_charges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_line_serials_tenant_id_serial_number",
                table: "purchase_return_line_serials",
                columns: new[] { "tenant_id", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_line_taxes_tenant_id_type",
                table: "purchase_return_line_taxes",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_lines_product_id",
                table: "purchase_return_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_lines_purchase_return_id_line_number",
                table: "purchase_return_lines",
                columns: new[] { "purchase_return_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_lines_tenant_id",
                table: "purchase_return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_lines_unit_id",
                table: "purchase_return_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_bill_id",
                table: "purchase_returns",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_financial_year_id",
                table: "purchase_returns",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_firm_id_date",
                table: "purchase_returns",
                columns: new[] { "firm_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_firm_id_number",
                table: "purchase_returns",
                columns: new[] { "firm_id", "number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_firm_id_supplier_ledger_id_date",
                table: "purchase_returns",
                columns: new[] { "firm_id", "supplier_ledger_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_firm_id_supplier_ledger_id_supplier_invoic~",
                table: "purchase_returns",
                columns: new[] { "firm_id", "supplier_ledger_id", "supplier_invoice_number" },
                unique: true,
                filter: "supplier_invoice_number IS NOT NULL AND is_deleted = false AND status <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_is_deleted",
                table: "purchase_returns",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_journal_voucher_id",
                table: "purchase_returns",
                column: "journal_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_receipt_note_id",
                table: "purchase_returns",
                column: "receipt_note_id",
                filter: "receipt_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_returns_invoice_id",
                table: "purchase_returns",
                column: "returns_invoice_id",
                filter: "returns_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_stock_document_id",
                table: "purchase_returns",
                column: "stock_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_supplier_ledger_id",
                table: "purchase_returns",
                column: "supplier_ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_tenant_id",
                table: "purchase_returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_warehouse_id",
                table: "purchase_returns",
                column: "warehouse_id");

            // The returns already recorded move to their own tables, tenant by tenant (the
            // tables are behind row-level security): the master, its details with their tax
            // and serial numbers, and its additional charges. Columns are matched by name,
            // so the copy follows the tables as they are, whatever order EF gave them. A
            // return names a purchase; one that named another return - which nothing should
            // have made - keeps its goods and figures but loses that link, since the return
            // it named is no purchase.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pg_temp.copy_rows(source text, target text, renamed_from text, renamed_to text, condition text)
                RETURNS void LANGUAGE plpgsql AS $fn$
                DECLARE
                    target_columns text;
                    source_columns text;
                BEGIN
                    SELECT string_agg(quote_ident(c.column_name), ', ' ORDER BY c.ordinal_position),
                           string_agg(quote_ident(CASE WHEN c.column_name = renamed_to THEN renamed_from ELSE c.column_name END), ', ' ORDER BY c.ordinal_position)
                    INTO target_columns, source_columns
                    FROM information_schema.columns c
                    WHERE c.table_schema = 'public' AND c.table_name = target AND c.is_generated = 'NEVER';

                    EXECUTE format('INSERT INTO %I (%s) SELECT %s FROM %I WHERE %s', target, target_columns, source_columns, source, condition);
                END $fn$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        UPDATE purchase_invoices SET returns_invoice_id = NULL
                        WHERE kind = 2 AND returns_invoice_id IN (SELECT id FROM purchase_invoices WHERE kind = 2);

                        PERFORM pg_temp.copy_rows('purchase_invoices', 'purchase_returns', '', '', 'kind = 2');
                        PERFORM pg_temp.copy_rows('purchase_invoice_lines', 'purchase_return_lines', 'purchase_invoice_id', 'purchase_return_id',
                            'purchase_invoice_id IN (SELECT id FROM purchase_returns)');
                        PERFORM pg_temp.copy_rows('purchase_invoice_line_taxes', 'purchase_return_line_taxes', 'purchase_invoice_line_id', 'purchase_return_line_id',
                            'purchase_invoice_line_id IN (SELECT id FROM purchase_return_lines)');
                        PERFORM pg_temp.copy_rows('purchase_invoice_line_serials', 'purchase_return_line_serials', 'purchase_invoice_line_id', 'purchase_return_line_id',
                            'purchase_invoice_line_id IN (SELECT id FROM purchase_return_lines)');
                        PERFORM pg_temp.copy_rows('purchase_invoice_charges', 'purchase_return_charges', 'purchase_invoice_id', 'purchase_return_id',
                            'purchase_invoice_id IN (SELECT id FROM purchase_returns)');

                        DELETE FROM purchase_invoice_line_taxes WHERE purchase_invoice_line_id IN (SELECT id FROM purchase_return_lines);
                        DELETE FROM purchase_invoice_line_serials WHERE purchase_invoice_line_id IN (SELECT id FROM purchase_return_lines);
                        DELETE FROM purchase_invoice_lines WHERE purchase_invoice_id IN (SELECT id FROM purchase_returns);
                        DELETE FROM purchase_invoice_charges WHERE purchase_invoice_id IN (SELECT id FROM purchase_returns);
                        DELETE FROM purchase_invoices WHERE kind = 2;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);

            // Only once the returns have moved: the purchase master keeps purchases alone, and an
            // RMA's debit note - a return - is held to the returns.
            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_invoices_kind",
                table: "purchase_invoices",
                sql: "kind = 1");

            migrationBuilder.AddForeignKey(
                name: "fk_rmas_purchase_returns_debit_note_id",
                table: "rmas",
                column: "debit_note_id",
                principalTable: "purchase_returns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            foreach (string table in ReturnTables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE public."{table}" FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS "{table}_tenant_isolation" ON public."{table}";

                    CREATE POLICY "{table}_tenant_isolation" ON public."{table}"
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_invoices_kind",
                table: "purchase_invoices");

            // Undone, the returns go back among the purchases before their tables are dropped.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pg_temp.copy_rows(source text, target text, renamed_from text, renamed_to text, condition text)
                RETURNS void LANGUAGE plpgsql AS $fn$
                DECLARE
                    target_columns text;
                    source_columns text;
                BEGIN
                    SELECT string_agg(quote_ident(c.column_name), ', ' ORDER BY c.ordinal_position),
                           string_agg(quote_ident(CASE WHEN c.column_name = renamed_to THEN renamed_from ELSE c.column_name END), ', ' ORDER BY c.ordinal_position)
                    INTO target_columns, source_columns
                    FROM information_schema.columns c
                    WHERE c.table_schema = 'public' AND c.table_name = target AND c.is_generated = 'NEVER';

                    EXECUTE format('INSERT INTO %I (%s) SELECT %s FROM %I WHERE %s', target, target_columns, source_columns, source, condition);
                END $fn$;

                DO $$
                DECLARE
                    tenant record;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        PERFORM pg_temp.copy_rows('purchase_returns', 'purchase_invoices', '', '', 'true');
                        PERFORM pg_temp.copy_rows('purchase_return_lines', 'purchase_invoice_lines', 'purchase_return_id', 'purchase_invoice_id', 'true');
                        PERFORM pg_temp.copy_rows('purchase_return_line_taxes', 'purchase_invoice_line_taxes', 'purchase_return_line_id', 'purchase_invoice_line_id', 'true');
                        PERFORM pg_temp.copy_rows('purchase_return_line_serials', 'purchase_invoice_line_serials', 'purchase_return_line_id', 'purchase_invoice_line_id', 'true');
                        PERFORM pg_temp.copy_rows('purchase_return_charges', 'purchase_invoice_charges', 'purchase_return_id', 'purchase_invoice_id', 'true');
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);

            // The keys go back to their earlier names.
            migrationBuilder.Sql("""
                ALTER TABLE purchase_invoice_charges RENAME CONSTRAINT "FK_purchase_invoice_charges_ledgers_ledger_id" TO "fk_purchase_invoice_charges_ledgers_ledger_id";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "FK_purchase_invoice_lines_products_product_id" TO "fk_purchase_invoice_lines_products_product_id";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "FK_purchase_invoice_lines_units_of_measure_unit_id" TO "fk_purchase_invoice_lines_units_of_measure_unit_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_bills_bill_id" TO "fk_purchase_invoices_bills_bill_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_financial_years_financial_year_id" TO "fk_purchase_invoices_financial_years_financial_year_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_ledgers_supplier_ledger_id" TO "fk_purchase_invoices_ledgers_supplier_ledger_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_stock_documents_receipt_note_id" TO "fk_purchase_invoices_stock_documents_receipt_note_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_stock_documents_stock_document_id" TO "fk_purchase_invoices_stock_documents_stock_document_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_vouchers_journal_voucher_id" TO "fk_purchase_invoices_vouchers_journal_voucher_id";
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "FK_purchase_invoices_warehouses_warehouse_id" TO "fk_purchase_invoices_warehouses_warehouse_id";
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_rmas_purchase_returns_debit_note_id",
                table: "rmas");

            migrationBuilder.DropTable(
                name: "purchase_return_charges");

            migrationBuilder.DropTable(
                name: "purchase_return_line_serials");

            migrationBuilder.DropTable(
                name: "purchase_return_line_taxes");

            migrationBuilder.DropTable(
                name: "purchase_return_lines");

            migrationBuilder.DropTable(
                name: "purchase_returns");

            // And the primary keys.
            migrationBuilder.Sql("""
                ALTER TABLE purchase_invoices RENAME CONSTRAINT "PK_purchase_invoices" TO "pk_purchase_invoices";
                ALTER TABLE purchase_invoice_lines RENAME CONSTRAINT "PK_purchase_invoice_lines" TO "pk_purchase_invoice_lines";
                ALTER TABLE purchase_invoice_line_taxes RENAME CONSTRAINT "PK_purchase_invoice_line_taxes" TO "pk_purchase_invoice_line_taxes";
                ALTER TABLE purchase_invoice_line_serials RENAME CONSTRAINT "PK_purchase_invoice_line_serials" TO "pk_purchase_invoice_line_serials";
                ALTER TABLE purchase_invoice_charges RENAME CONSTRAINT "PK_purchase_invoice_charges" TO "pk_purchase_invoice_charges";
                """);

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_warehouse_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_supplier_ledger_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_supplier_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_stock_document_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_stock_document_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_journal_voucher_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_journal_voucher_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_is_deleted",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_is_deleted");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_financial_year_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_financial_year_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_bill_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_bill_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_tenant_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_returns_invoice_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_returns_invoice");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_receipt_note_id",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_receipt_note");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_firm_id_supplier_ledger_id_supplier_invoi~",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_supplier_number");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_firm_id_supplier_ledger_id_date",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_supplier");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_firm_id_number",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_number");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoices_firm_id_date",
                table: "purchase_invoices",
                newName: "ix_purchase_invoices_date");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_lines_unit_id",
                table: "purchase_invoice_lines",
                newName: "ix_purchase_invoice_lines_unit_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_lines_tenant_id",
                table: "purchase_invoice_lines",
                newName: "ix_purchase_invoice_lines_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_lines_purchase_invoice_id_line_number",
                table: "purchase_invoice_lines",
                newName: "ix_purchase_invoice_lines_invoice");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_lines_product_id",
                table: "purchase_invoice_lines",
                newName: "ix_purchase_invoice_lines_product");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_line_taxes_tenant_id_type",
                table: "purchase_invoice_line_taxes",
                newName: "ix_purchase_invoice_line_taxes_component");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_line_serials_tenant_id_serial_number",
                table: "purchase_invoice_line_serials",
                newName: "ix_purchase_invoice_line_serials_serial");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_charges_ledger_id",
                table: "purchase_invoice_charges",
                newName: "ix_purchase_invoice_charges_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_charges_tenant_id",
                table: "purchase_invoice_charges",
                newName: "ix_purchase_invoice_charges_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_purchase_invoice_charges_purchase_invoice_id_ledger_id",
                table: "purchase_invoice_charges",
                newName: "ix_purchase_invoice_charges_ledger");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_invoices_purchase_invoices_returns_invoice_id",
                table: "purchase_invoices",
                column: "returns_invoice_id",
                principalTable: "purchase_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rmas_purchase_invoices_debit_note_id",
                table: "rmas",
                column: "debit_note_id",
                principalTable: "purchase_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
