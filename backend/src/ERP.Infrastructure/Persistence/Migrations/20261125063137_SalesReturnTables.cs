using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesReturnTables : Migration
    {
        private static readonly string[] ReturnTables =
        [
            "sales_returns", "sales_return_lines", "sales_return_line_taxes", "sales_return_line_serials", "sales_return_charges",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_rmas_sales_invoices_credit_note_id",
                table: "rmas");

            // The existing tables' keys are unchanged; they take the names EF Core gives keys
            // kept on more than one table, so the model and the database agree.
            migrationBuilder.Sql("""
                ALTER TABLE sales_invoice_charges RENAME CONSTRAINT "fk_sales_invoice_charges_ledgers_ledger_id" TO "FK_sales_invoice_charges_ledgers_ledger_id";
                ALTER TABLE sales_invoice_line_serials RENAME CONSTRAINT "fk_sales_invoice_line_serials_serial_numbers_serial_number_id" TO "FK_sales_invoice_line_serials_serial_numbers_serial_number_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "fk_sales_invoice_lines_batches_batch_id" TO "FK_sales_invoice_lines_batches_batch_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "fk_sales_invoice_lines_products_product_id" TO "FK_sales_invoice_lines_products_product_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "fk_sales_invoice_lines_units_of_measure_unit_id" TO "FK_sales_invoice_lines_units_of_measure_unit_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_bills_bill_id" TO "FK_sales_invoices_bills_bill_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_financial_years_financial_year_id" TO "FK_sales_invoices_financial_years_financial_year_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_ledgers_billing_man_ledger_id" TO "FK_sales_invoices_ledgers_billing_man_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_ledgers_customer_ledger_id" TO "FK_sales_invoices_ledgers_customer_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_ledgers_received_into_ledger_id" TO "FK_sales_invoices_ledgers_received_into_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_ledgers_salesman_ledger_id" TO "FK_sales_invoices_ledgers_salesman_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_sales_orders_sales_order_id" TO "FK_sales_invoices_sales_orders_sales_order_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_stock_documents_delivery_note_id" TO "FK_sales_invoices_stock_documents_delivery_note_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_stock_documents_stock_document_id" TO "FK_sales_invoices_stock_documents_stock_document_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_vouchers_journal_voucher_id" TO "FK_sales_invoices_vouchers_journal_voucher_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_vouchers_receipt_voucher_id" TO "FK_sales_invoices_vouchers_receipt_voucher_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "fk_sales_invoices_warehouses_warehouse_id" TO "FK_sales_invoices_warehouses_warehouse_id";
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_sales_invoices_returns_invoice_id",
                table: "sales_invoices");

            // Likewise their primary keys: renamed, not rebuilt - other tables' keys depend on them.
            migrationBuilder.Sql("""
                ALTER TABLE sales_invoices RENAME CONSTRAINT "pk_sales_invoices" TO "PK_sales_invoices";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "pk_sales_invoice_lines" TO "PK_sales_invoice_lines";
                ALTER TABLE sales_invoice_line_taxes RENAME CONSTRAINT "pk_sales_invoice_line_taxes" TO "PK_sales_invoice_line_taxes";
                ALTER TABLE sales_invoice_line_serials RENAME CONSTRAINT "pk_sales_invoice_line_serials" TO "PK_sales_invoice_line_serials";
                ALTER TABLE sales_invoice_charges RENAME CONSTRAINT "pk_sales_invoice_charges" TO "PK_sales_invoice_charges";
                """);

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_warehouse_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_stock_document_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_stock_document_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_salesman_ledger_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_salesman_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_received_into_ledger_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_received_into_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_receipt_voucher_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_receipt_voucher_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_journal_voucher_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_journal_voucher_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_is_deleted",
                table: "sales_invoices",
                newName: "IX_sales_invoices_is_deleted");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_financial_year_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_financial_year_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_customer_ledger_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_customer_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_billing_man_ledger_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_billing_man_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_bill_id",
                table: "sales_invoices",
                newName: "IX_sales_invoices_bill_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_tenant",
                table: "sales_invoices",
                newName: "IX_sales_invoices_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_returns_invoice",
                table: "sales_invoices",
                newName: "IX_sales_invoices_returns_invoice_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_order",
                table: "sales_invoices",
                newName: "IX_sales_invoices_sales_order_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_number",
                table: "sales_invoices",
                newName: "IX_sales_invoices_firm_id_number");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_delivery_note",
                table: "sales_invoices",
                newName: "IX_sales_invoices_delivery_note_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_date",
                table: "sales_invoices",
                newName: "IX_sales_invoices_firm_id_date");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoices_customer",
                table: "sales_invoices",
                newName: "IX_sales_invoices_firm_id_customer_ledger_id_date");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_lines_unit_id",
                table: "sales_invoice_lines",
                newName: "IX_sales_invoice_lines_unit_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_lines_batch_id",
                table: "sales_invoice_lines",
                newName: "IX_sales_invoice_lines_batch_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_lines_tenant",
                table: "sales_invoice_lines",
                newName: "IX_sales_invoice_lines_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_lines_product",
                table: "sales_invoice_lines",
                newName: "IX_sales_invoice_lines_product_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_lines_invoice",
                table: "sales_invoice_lines",
                newName: "IX_sales_invoice_lines_sales_invoice_id_line_number");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_line_taxes_component",
                table: "sales_invoice_line_taxes",
                newName: "IX_sales_invoice_line_taxes_tenant_id_type");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_line_serials_tenant",
                table: "sales_invoice_line_serials",
                newName: "IX_sales_invoice_line_serials_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_line_serials_serial",
                table: "sales_invoice_line_serials",
                newName: "IX_sales_invoice_line_serials_serial_number_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_charges_ledger_id",
                table: "sales_invoice_charges",
                newName: "IX_sales_invoice_charges_ledger_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_charges_tenant",
                table: "sales_invoice_charges",
                newName: "IX_sales_invoice_charges_tenant_id");

            migrationBuilder.RenameIndex(
                name: "ix_sales_invoice_charges_ledger",
                table: "sales_invoice_charges",
                newName: "IX_sales_invoice_charges_sales_invoice_id_ledger_id");

            migrationBuilder.CreateTable(
                name: "sales_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    returns_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    payment_mode = table.Column<int>(type: "integer", nullable: false),
                    amount_received = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    received_into_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    online_payment_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    receipt_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    salesman_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    billing_man_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    billing_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    billing_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    billing_address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    area_id = table.Column<Guid>(type: "uuid", nullable: true),
                    route_id = table.Column<Guid>(type: "uuid", nullable: true),
                    round_to = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, defaultValue: 0m),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_note_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_sales_returns", x => x.id);
                    table.CheckConstraint("ck_sales_returns_kind", "kind = 2");
                    table.ForeignKey(
                        name: "FK_sales_returns_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_ledgers_billing_man_ledger_id",
                        column: x => x.billing_man_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_ledgers_customer_ledger_id",
                        column: x => x.customer_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_ledgers_received_into_ledger_id",
                        column: x => x.received_into_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_ledgers_salesman_ledger_id",
                        column: x => x.salesman_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_stock_documents_delivery_note_id",
                        column: x => x.delivery_note_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_vouchers_journal_voucher_id",
                        column: x => x.journal_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_vouchers_receipt_voucher_id",
                        column: x => x.receipt_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_returns_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_sales_invoices_returns_invoice_id",
                        column: x => x.returns_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_charges", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_return_charges_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_charges_sales_returns_sales_invoice_id",
                        column: x => x.sales_return_id,
                        principalTable: "sales_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_sales_return_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_return_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_return_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_return_lines_units_of_measure_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_lines_sales_returns_sales_invoice_id",
                        column: x => x.sales_return_id,
                        principalTable: "sales_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_line_serials",
                columns: table => new
                {
                    sales_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_line_serials", x => new { x.sales_return_line_id, x.serial_number_id });
                    table.ForeignKey(
                        name: "FK_sales_return_line_serials_serial_numbers_serial_number_id",
                        column: x => x.serial_number_id,
                        principalTable: "serial_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_line_serials_sales_return_lines_sales_invoice_~",
                        column: x => x.sales_return_line_id,
                        principalTable: "sales_return_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_line_taxes",
                columns: table => new
                {
                    sales_return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_line_taxes", x => new { x.sales_return_line_id, x.type });
                    table.ForeignKey(
                        name: "fk_sales_return_line_taxes_sales_return_lines_sales_invoice_li~",
                        column: x => x.sales_return_line_id,
                        principalTable: "sales_return_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_charges_ledger_id",
                table: "sales_return_charges",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_charges_sales_return_id_ledger_id",
                table: "sales_return_charges",
                columns: new[] { "sales_return_id", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_charges_tenant_id",
                table: "sales_return_charges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_line_serials_serial_number_id",
                table: "sales_return_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_line_serials_tenant_id",
                table: "sales_return_line_serials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_line_taxes_tenant_id_type",
                table: "sales_return_line_taxes",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_batch_id",
                table: "sales_return_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_product_id",
                table: "sales_return_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_sales_return_id_line_number",
                table: "sales_return_lines",
                columns: new[] { "sales_return_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_tenant_id",
                table: "sales_return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_lines_unit_id",
                table: "sales_return_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_bill_id",
                table: "sales_returns",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_billing_man_ledger_id",
                table: "sales_returns",
                column: "billing_man_ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_customer_ledger_id",
                table: "sales_returns",
                column: "customer_ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_delivery_note_id",
                table: "sales_returns",
                column: "delivery_note_id",
                filter: "delivery_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_financial_year_id",
                table: "sales_returns",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_firm_id_customer_ledger_id_date",
                table: "sales_returns",
                columns: new[] { "firm_id", "customer_ledger_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_firm_id_date",
                table: "sales_returns",
                columns: new[] { "firm_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_firm_id_number",
                table: "sales_returns",
                columns: new[] { "firm_id", "number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_is_deleted",
                table: "sales_returns",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_journal_voucher_id",
                table: "sales_returns",
                column: "journal_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_receipt_voucher_id",
                table: "sales_returns",
                column: "receipt_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_received_into_ledger_id",
                table: "sales_returns",
                column: "received_into_ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_returns_invoice_id",
                table: "sales_returns",
                column: "returns_invoice_id",
                filter: "returns_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_sales_order_id",
                table: "sales_returns",
                column: "sales_order_id",
                filter: "sales_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_salesman_ledger_id",
                table: "sales_returns",
                column: "salesman_ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_stock_document_id",
                table: "sales_returns",
                column: "stock_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_tenant_id",
                table: "sales_returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_returns_warehouse_id",
                table: "sales_returns",
                column: "warehouse_id");

            // The returns already recorded move to their own tables, tenant by tenant (the
            // tables are behind row-level security): the master, its details with their tax
            // and serial numbers, and its additional charges. Columns are matched by name,
            // so the copy follows the tables as they are, whatever order EF gave them. A
            // return names a sale; one that named another return - which nothing should
            // have made - keeps its goods and figures but loses that link, since the return
            // it named is no sale.
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

                        UPDATE sales_invoices SET returns_invoice_id = NULL
                        WHERE kind = 2 AND returns_invoice_id IN (SELECT id FROM sales_invoices WHERE kind = 2);

                        PERFORM pg_temp.copy_rows('sales_invoices', 'sales_returns', '', '', 'kind = 2');
                        PERFORM pg_temp.copy_rows('sales_invoice_lines', 'sales_return_lines', 'sales_invoice_id', 'sales_return_id',
                            'sales_invoice_id IN (SELECT id FROM sales_returns)');
                        PERFORM pg_temp.copy_rows('sales_invoice_line_taxes', 'sales_return_line_taxes', 'sales_invoice_line_id', 'sales_return_line_id',
                            'sales_invoice_line_id IN (SELECT id FROM sales_return_lines)');
                        PERFORM pg_temp.copy_rows('sales_invoice_line_serials', 'sales_return_line_serials', 'sales_invoice_line_id', 'sales_return_line_id',
                            'sales_invoice_line_id IN (SELECT id FROM sales_return_lines)');
                        PERFORM pg_temp.copy_rows('sales_invoice_charges', 'sales_return_charges', 'sales_invoice_id', 'sales_return_id',
                            'sales_invoice_id IN (SELECT id FROM sales_returns)');

                        DELETE FROM sales_invoice_line_taxes WHERE sales_invoice_line_id IN (SELECT id FROM sales_return_lines);
                        DELETE FROM sales_invoice_line_serials WHERE sales_invoice_line_id IN (SELECT id FROM sales_return_lines);
                        DELETE FROM sales_invoice_lines WHERE sales_invoice_id IN (SELECT id FROM sales_returns);
                        DELETE FROM sales_invoice_charges WHERE sales_invoice_id IN (SELECT id FROM sales_returns);
                        DELETE FROM sales_invoices WHERE kind = 2;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);

            // Only once the returns have moved: the sales master keeps sales alone, and an
            // RMA's credit note - a return - is held to the returns.
            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_invoices_kind",
                table: "sales_invoices",
                sql: "kind = 1");

            migrationBuilder.AddForeignKey(
                name: "fk_rmas_sales_returns_credit_note_id",
                table: "rmas",
                column: "credit_note_id",
                principalTable: "sales_returns",
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
                name: "ck_sales_invoices_kind",
                table: "sales_invoices");

            // Undone, the returns go back among the sales before their tables are dropped.
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

                        PERFORM pg_temp.copy_rows('sales_returns', 'sales_invoices', '', '', 'true');
                        PERFORM pg_temp.copy_rows('sales_return_lines', 'sales_invoice_lines', 'sales_return_id', 'sales_invoice_id', 'true');
                        PERFORM pg_temp.copy_rows('sales_return_line_taxes', 'sales_invoice_line_taxes', 'sales_return_line_id', 'sales_invoice_line_id', 'true');
                        PERFORM pg_temp.copy_rows('sales_return_line_serials', 'sales_invoice_line_serials', 'sales_return_line_id', 'sales_invoice_line_id', 'true');
                        PERFORM pg_temp.copy_rows('sales_return_charges', 'sales_invoice_charges', 'sales_return_id', 'sales_invoice_id', 'true');
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_rmas_sales_returns_credit_note_id",
                table: "rmas");

            // The keys go back to their earlier names.
            migrationBuilder.Sql("""
                ALTER TABLE sales_invoice_charges RENAME CONSTRAINT "FK_sales_invoice_charges_ledgers_ledger_id" TO "fk_sales_invoice_charges_ledgers_ledger_id";
                ALTER TABLE sales_invoice_line_serials RENAME CONSTRAINT "FK_sales_invoice_line_serials_serial_numbers_serial_number_id" TO "fk_sales_invoice_line_serials_serial_numbers_serial_number_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "FK_sales_invoice_lines_batches_batch_id" TO "fk_sales_invoice_lines_batches_batch_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "FK_sales_invoice_lines_products_product_id" TO "fk_sales_invoice_lines_products_product_id";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "FK_sales_invoice_lines_units_of_measure_unit_id" TO "fk_sales_invoice_lines_units_of_measure_unit_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_bills_bill_id" TO "fk_sales_invoices_bills_bill_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_financial_years_financial_year_id" TO "fk_sales_invoices_financial_years_financial_year_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_ledgers_billing_man_ledger_id" TO "fk_sales_invoices_ledgers_billing_man_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_ledgers_customer_ledger_id" TO "fk_sales_invoices_ledgers_customer_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_ledgers_received_into_ledger_id" TO "fk_sales_invoices_ledgers_received_into_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_ledgers_salesman_ledger_id" TO "fk_sales_invoices_ledgers_salesman_ledger_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_sales_orders_sales_order_id" TO "fk_sales_invoices_sales_orders_sales_order_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_stock_documents_delivery_note_id" TO "fk_sales_invoices_stock_documents_delivery_note_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_stock_documents_stock_document_id" TO "fk_sales_invoices_stock_documents_stock_document_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_vouchers_journal_voucher_id" TO "fk_sales_invoices_vouchers_journal_voucher_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_vouchers_receipt_voucher_id" TO "fk_sales_invoices_vouchers_receipt_voucher_id";
                ALTER TABLE sales_invoices RENAME CONSTRAINT "FK_sales_invoices_warehouses_warehouse_id" TO "fk_sales_invoices_warehouses_warehouse_id";
                """);

            migrationBuilder.DropTable(
                name: "sales_return_charges");

            migrationBuilder.DropTable(
                name: "sales_return_line_serials");

            migrationBuilder.DropTable(
                name: "sales_return_line_taxes");

            migrationBuilder.DropTable(
                name: "sales_return_lines");

            migrationBuilder.DropTable(
                name: "sales_returns");

            // And the primary keys.
            migrationBuilder.Sql("""
                ALTER TABLE sales_invoices RENAME CONSTRAINT "PK_sales_invoices" TO "pk_sales_invoices";
                ALTER TABLE sales_invoice_lines RENAME CONSTRAINT "PK_sales_invoice_lines" TO "pk_sales_invoice_lines";
                ALTER TABLE sales_invoice_line_taxes RENAME CONSTRAINT "PK_sales_invoice_line_taxes" TO "pk_sales_invoice_line_taxes";
                ALTER TABLE sales_invoice_line_serials RENAME CONSTRAINT "PK_sales_invoice_line_serials" TO "pk_sales_invoice_line_serials";
                ALTER TABLE sales_invoice_charges RENAME CONSTRAINT "PK_sales_invoice_charges" TO "pk_sales_invoice_charges";
                """);

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_warehouse_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_stock_document_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_stock_document_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_salesman_ledger_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_salesman_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_received_into_ledger_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_received_into_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_receipt_voucher_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_receipt_voucher_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_journal_voucher_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_journal_voucher_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_is_deleted",
                table: "sales_invoices",
                newName: "ix_sales_invoices_is_deleted");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_financial_year_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_financial_year_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_customer_ledger_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_customer_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_billing_man_ledger_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_billing_man_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_bill_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_bill_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_tenant_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_sales_order_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_order");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_returns_invoice_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_returns_invoice");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_firm_id_number",
                table: "sales_invoices",
                newName: "ix_sales_invoices_number");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_firm_id_date",
                table: "sales_invoices",
                newName: "ix_sales_invoices_date");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_firm_id_customer_ledger_id_date",
                table: "sales_invoices",
                newName: "ix_sales_invoices_customer");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoices_delivery_note_id",
                table: "sales_invoices",
                newName: "ix_sales_invoices_delivery_note");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_lines_unit_id",
                table: "sales_invoice_lines",
                newName: "ix_sales_invoice_lines_unit_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_lines_batch_id",
                table: "sales_invoice_lines",
                newName: "ix_sales_invoice_lines_batch_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_lines_tenant_id",
                table: "sales_invoice_lines",
                newName: "ix_sales_invoice_lines_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_lines_sales_invoice_id_line_number",
                table: "sales_invoice_lines",
                newName: "ix_sales_invoice_lines_invoice");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_lines_product_id",
                table: "sales_invoice_lines",
                newName: "ix_sales_invoice_lines_product");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_line_taxes_tenant_id_type",
                table: "sales_invoice_line_taxes",
                newName: "ix_sales_invoice_line_taxes_component");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_line_serials_tenant_id",
                table: "sales_invoice_line_serials",
                newName: "ix_sales_invoice_line_serials_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_line_serials_serial_number_id",
                table: "sales_invoice_line_serials",
                newName: "ix_sales_invoice_line_serials_serial");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_charges_ledger_id",
                table: "sales_invoice_charges",
                newName: "ix_sales_invoice_charges_ledger_id");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_charges_tenant_id",
                table: "sales_invoice_charges",
                newName: "ix_sales_invoice_charges_tenant");

            migrationBuilder.RenameIndex(
                name: "IX_sales_invoice_charges_sales_invoice_id_ledger_id",
                table: "sales_invoice_charges",
                newName: "ix_sales_invoice_charges_ledger");

            migrationBuilder.AddForeignKey(
                name: "fk_rmas_sales_invoices_credit_note_id",
                table: "rmas",
                column: "credit_note_id",
                principalTable: "sales_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_sales_invoices_returns_invoice_id",
                table: "sales_invoices",
                column: "returns_invoice_id",
                principalTable: "sales_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
