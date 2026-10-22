using System.Globalization;
using System.Text;
using ERP.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Regroups every existing firm's menu into the catalogue's headings: Sales,
    /// Purchase, Accounts and Inventory in place of one Transactions heading, every
    /// report under Reports, and a Ledgers entry under Masters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A migration rather than seeding, because seeding runs only where it is switched
    /// on and only for the firm it seeds, and a menu is a firm's own data. Adds the
    /// entries a firm does not have, moves each entry still where the previous menu put
    /// it, applies the new labels and routes where the old ones are unchanged, and
    /// removes a system heading left with nothing beneath it. An entry an administrator
    /// has moved, renamed or pointed somewhere else keeps their choice.
    /// </para>
    /// <para>
    /// Run for each tenant in turn, because every table it touches is behind row-level
    /// security and the connection sees one tenant at a time.
    /// </para>
    /// </remarks>
    public partial class MenuRestructure : Migration
    {
        /// <summary>The system actor, recorded as the creator of entries added here.</summary>
        private const string SystemUser = "00000000-0000-0000-0000-00000000513e";

        /// <summary>Where the previous menu placed entries that now sit somewhere else.</summary>
        /// <remarks>Null for a top-level entry. Entries missing here did not move.</remarks>
        private static readonly Dictionary<string, string> PreviousParents = new(StringComparer.Ordinal)
        {
            ["masters.countries"] = "masters.accounts",
            ["masters.territories"] = "masters.accounts",
            ["transactions.receipts"] = "transactions",
            ["transactions.payments"] = "transactions",
            ["transactions.voucher-entry"] = "transactions",
            ["transactions.stock"] = "transactions",
            ["transactions.stock-transfers"] = "transactions",
            ["transactions.stock-adjustment"] = "transactions",
            ["transactions.production"] = "transactions",
            ["transactions.damaged-stock"] = "transactions",
            ["transactions.sales-quotations"] = "transactions",
            ["transactions.sales-orders"] = "transactions",
            ["transactions.delivery-notes"] = "transactions",
            ["transactions.sales"] = "transactions",
            ["transactions.sales-returns"] = "transactions",
            ["transactions.purchase-orders"] = "transactions",
            ["transactions.receipt-notes"] = "transactions",
            ["transactions.purchase"] = "transactions",
            ["transactions.purchase-returns"] = "transactions",
            ["cheques"] = null,
            ["accounts-reports"] = null,
            ["inventory-reports"] = null,
            ["accounts-reports.trial-balance"] = "accounts-reports",
            ["accounts-reports.group-summary"] = "accounts-reports",
            ["accounts-reports.profit-and-loss"] = "accounts-reports",
            ["accounts-reports.balance-sheet"] = "accounts-reports",
            ["accounts-reports.cash-flow"] = "accounts-reports",
            ["accounts-reports.tax-returns"] = "accounts-reports",
        };

        /// <summary>Labels and routes the previous menu used, and what replaces them.</summary>
        private static readonly (string Code, string Field, string From, string To)[] Renames =
        [
            ("transactions.receipts", "label", "Receipt", "Receipts"),
            ("transactions.receipts", "label_arabic", "سند قبض", "سندات القبض"),
            ("transactions.receipts", "route", "/accounting/receipts/new", "/accounting/receipts"),
            ("transactions.payments", "label", "Payment", "Payments"),
            ("transactions.payments", "label_arabic", "سند صرف", "سندات الصرف"),
            ("transactions.payments", "route", "/accounting/payments/new", "/accounting/payments"),
            ("transactions.voucher-entry", "label", "Voucher entry", "Journal"),
            ("transactions.voucher-entry", "label_arabic", "إدخال قيد", "قيود اليومية"),
            ("transactions.voucher-entry", "route", "/accounting/vouchers/new", "/accounting/journals"),
            ("transactions.sales-quotations", "label", "Sales quotations", "Quotations"),
            ("accounts-reports", "label", "Accounts reports", "Accounts"),
            ("inventory-reports", "label", "Stock reports", "Stock"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            List<string> rows = [];
            Collect(MenuCatalogue.Default, parentCode: null, depth: 0, rows);

            string renames = string.Join(
                ",\n",
                Renames.Select(r =>
                    $"({Quote(r.Code)}, {Quote(r.Field)}, {Quote(r.From)}, {Quote(r.To)})"));

            migrationBuilder.Sql($"""
                CREATE TEMP TABLE menu_target (
                    code text PRIMARY KEY,
                    parent_code text,
                    previous_parent_code text,
                    label text NOT NULL,
                    label_arabic text,
                    module text NOT NULL,
                    route text,
                    permission text,
                    sort_order integer NOT NULL,
                    depth integer NOT NULL
                ) ON COMMIT DROP;

                INSERT INTO menu_target VALUES
                {string.Join(",\n", rows)};

                CREATE TEMP TABLE menu_renames (
                    code text NOT NULL,
                    field text NOT NULL,
                    old_value text NOT NULL,
                    new_value text NOT NULL
                ) ON COMMIT DROP;

                INSERT INTO menu_renames VALUES
                {renames};

                DO $$
                DECLARE
                    tenant record;
                    firm record;
                    level integer;
                BEGIN
                    FOR tenant IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.current_tenant', tenant.id::text, true);

                        FOR firm IN SELECT DISTINCT firm_id FROM menu_items LOOP
                            -- Headings first, so the entries beneath them have a parent.
                            FOR level IN 0..3 LOOP
                                INSERT INTO menu_items (
                                    id, tenant_id, firm_id, parent_id, code, label,
                                    label_arabic, icon, route, module, sort_order,
                                    required_permission, is_enabled, is_system,
                                    created_at_utc, created_by)
                                SELECT gen_random_uuid(), tenant.id, firm.firm_id, parent.id,
                                       target.code, target.label, target.label_arabic, NULL,
                                       target.route, target.module, target.sort_order,
                                       target.permission, true, true, now(), '{SystemUser}'::uuid
                                FROM menu_target target
                                LEFT JOIN menu_items parent
                                    ON parent.firm_id = firm.firm_id
                                   AND parent.code = target.parent_code
                                WHERE target.depth = level
                                  AND (target.parent_code IS NULL OR parent.id IS NOT NULL)
                                  AND NOT EXISTS (
                                      SELECT 1 FROM menu_items existing
                                      WHERE existing.firm_id = firm.firm_id
                                        AND existing.code = target.code);
                            END LOOP;

                            -- Moved and reordered only while still where the previous
                            -- menu (or this one) put it.
                            UPDATE menu_items item
                            SET parent_id = destination.id,
                                sort_order = target.sort_order
                            FROM menu_target target
                            LEFT JOIN menu_items destination
                                ON destination.firm_id = firm.firm_id
                               AND destination.code = target.parent_code
                            WHERE item.firm_id = firm.firm_id
                              AND item.code = target.code
                              AND (target.parent_code IS NULL OR destination.id IS NOT NULL)
                              AND (
                                  (SELECT holder.code FROM menu_items holder
                                   WHERE holder.id = item.parent_id)
                                      IS NOT DISTINCT FROM target.previous_parent_code
                                  OR (SELECT holder.code FROM menu_items holder
                                      WHERE holder.id = item.parent_id)
                                      IS NOT DISTINCT FROM target.parent_code);

                            UPDATE menu_items item SET label = r.new_value
                            FROM menu_renames r
                            WHERE item.firm_id = firm.firm_id AND item.code = r.code
                              AND r.field = 'label' AND item.label = r.old_value;

                            UPDATE menu_items item SET label_arabic = r.new_value
                            FROM menu_renames r
                            WHERE item.firm_id = firm.firm_id AND item.code = r.code
                              AND r.field = 'label_arabic'
                              AND item.label_arabic = r.old_value;

                            UPDATE menu_items item SET route = r.new_value
                            FROM menu_renames r
                            WHERE item.firm_id = firm.firm_id AND item.code = r.code
                              AND r.field = 'route' AND item.route = r.old_value;

                            -- A system heading the catalogue no longer has, emptied by
                            -- the moves above.
                            DELETE FROM menu_items heading
                            WHERE heading.firm_id = firm.firm_id
                              AND heading.is_system
                              AND heading.route IS NULL
                              AND NOT EXISTS (
                                  SELECT 1 FROM menu_target target
                                  WHERE target.code = heading.code)
                              AND NOT EXISTS (
                                  SELECT 1 FROM menu_items child
                                  WHERE child.parent_id = heading.id);
                        END LOOP;

                        -- A role that chose the chart of accounts is given the new list
                        -- of the same ledgers.
                        INSERT INTO role_menus (role_id, menu_code, tenant_id)
                        SELECT chosen.role_id, 'masters.ledger-list', chosen.tenant_id
                        FROM role_menus chosen
                        WHERE chosen.menu_code = 'masters.ledgers'
                        ON CONFLICT DO NOTHING;
                    END LOOP;

                    PERFORM set_config('app.current_tenant', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to undo: the previous arrangement is not recorded, and an administrator
        /// can regroup the menu from Menu settings.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: see the remarks.
        }

        private static void Collect(
            IReadOnlyList<MenuBlueprint> level,
            string parentCode,
            int depth,
            List<string> rows)
        {
            for (int index = 0; index < level.Count; index++)
            {
                MenuBlueprint entry = level[index];
                string previous = PreviousParents.TryGetValue(entry.Code, out string moved)
                    ? moved
                    : parentCode;

                StringBuilder row = new();
                row.Append(CultureInfo.InvariantCulture, $"({Quote(entry.Code)}, {Quote(parentCode)}, ")
                    .Append(CultureInfo.InvariantCulture, $"{Quote(previous)}, {Quote(entry.Label)}, ")
                    .Append(CultureInfo.InvariantCulture, $"{Quote(entry.LabelArabic)}, {Quote(entry.Module)}, ")
                    .Append(CultureInfo.InvariantCulture, $"{Quote(entry.Route)}, {Quote(entry.RequiredPermission)}, ")
                    .Append(CultureInfo.InvariantCulture, $"{(index + 1) * 10}, {depth})");
                rows.Add(row.ToString());

                if (entry.Children is { Count: > 0 } children)
                {
                    Collect(children, entry.Code, depth + 1, rows);
                }
            }
        }

        private static string Quote(string value) =>
            value is null ? "NULL" : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
