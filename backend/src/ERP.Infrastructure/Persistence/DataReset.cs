using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Platform.Data;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Persistence.Seeding;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ERP.Infrastructure.Persistence;

/// <summary>The PostgreSQL implementation of <see cref="IDataReset"/>.</summary>
/// <remarks>
/// <para>
/// Plain SQL, in the order the foreign keys demand, because the aggregates would refuse
/// most of it by design - a posted invoice cannot be deleted, only cancelled - and a reset
/// is precisely the one occasion that rule is meant not to apply. Every statement names the
/// firm, and the connection carries the tenant, so row-level security still confines it.
/// </para>
/// <para>
/// The ledgers the chart of accounts seeded are kept by their codes; everything else under
/// the firm's ledgers - customers, suppliers, anything added - goes with "everything".
/// </para>
/// </remarks>
public sealed class DataReset : IDataReset
{
    private static readonly string[] Transactions =
    [
        // Links back to other documents first, so the documents can go in any order.
        "UPDATE sales_invoices SET returns_invoice_id = NULL WHERE firm_id = @firm",
        "UPDATE purchase_invoices SET returns_invoice_id = NULL WHERE firm_id = @firm",
        "DELETE FROM sales_invoices WHERE firm_id = @firm",
        "DELETE FROM purchase_invoices WHERE firm_id = @firm",
        "DELETE FROM sales_orders WHERE firm_id = @firm",
        "DELETE FROM purchase_orders WHERE firm_id = @firm",
        "DELETE FROM cheques WHERE firm_id = @firm",
        "DELETE FROM bills WHERE firm_id = @firm",
        "DELETE FROM stock_ledger_entries WHERE firm_id = @firm",
        """
        DELETE FROM stock_document_line_serials WHERE stock_document_line_id IN (
            SELECT line.id FROM stock_document_lines line
            JOIN stock_documents document ON document.id = line.stock_document_id
            WHERE document.firm_id = @firm)
        """,
        "DELETE FROM serial_numbers WHERE firm_id = @firm",
        "DELETE FROM stock_documents WHERE firm_id = @firm",
        "DELETE FROM batch_balances WHERE firm_id = @firm",
        "DELETE FROM stock_balances WHERE firm_id = @firm",
        "DELETE FROM batches WHERE firm_id = @firm",
        "DELETE FROM vouchers WHERE firm_id = @firm",
        "UPDATE numbering_series SET next_number = starting_number WHERE firm_id = @firm",
    ];

    private static readonly string[] Masters =
    [
        "DELETE FROM products WHERE firm_id = @firm",
        "UPDATE categories SET parent_id = NULL WHERE firm_id = @firm",
        "DELETE FROM categories WHERE firm_id = @firm",
        "DELETE FROM brands WHERE firm_id = @firm",
        "UPDATE units_of_measure SET base_unit_id = NULL WHERE firm_id = @firm",
        "DELETE FROM units_of_measure WHERE firm_id = @firm",
        "DELETE FROM warehouses WHERE firm_id = @firm",
        """
        DELETE FROM additional_ledgers WHERE firm_id = @firm AND ledger_id IN (
            SELECT id FROM ledgers WHERE firm_id = @firm AND NOT (code = ANY(@kept)))
        """,
        """
        DELETE FROM inventory_account_assignments WHERE ledger_id IN (
            SELECT id FROM ledgers WHERE firm_id = @firm AND NOT (code = ANY(@kept)))
        """,
        """
        DELETE FROM tax_account_assignments WHERE ledger_id IN (
            SELECT id FROM ledgers WHERE firm_id = @firm AND NOT (code = ANY(@kept)))
        """,
        "DELETE FROM ledgers WHERE firm_id = @firm AND NOT (code = ANY(@kept))",
        "UPDATE ledgers SET opening_balance = 0 WHERE firm_id = @firm",
    ];

    private readonly ErpDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="DataReset"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public DataReset(ErpDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<DataResetPreview>> PreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<DataResetPreview>(Error.Forbidden(
                "DataReset.NoFirmSelected", "A firm must be selected to clear its data."));
        }

        Firm? firm = await _context.Firms.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<DataResetPreview>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        string[] kept = KeptLedgers(firm);

        Dictionary<string, int> transactions = new(StringComparer.Ordinal)
        {
            ["salesInvoices"] = await _context.SalesInvoices.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["purchaseInvoices"] = await _context.PurchaseInvoices.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["orders"] = await _context.SalesOrders.CountAsync(x => x.FirmId == firmId, cancellationToken)
                + await _context.PurchaseOrders.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["stockDocuments"] = await _context.StockDocuments.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["vouchers"] = await _context.Vouchers.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["cheques"] = await _context.Cheques.CountAsync(x => x.FirmId == firmId, cancellationToken),
        };

        Dictionary<string, int> masters = new(StringComparer.Ordinal)
        {
            ["products"] = await _context.Products.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["parties"] = await _context.Ledgers.CountAsync(
                x => x.FirmId == firmId && !kept.Contains(x.Code), cancellationToken),
            ["warehouses"] = await _context.Warehouses.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["categories"] = await _context.Categories.CountAsync(x => x.FirmId == firmId, cancellationToken),
            ["units"] = await _context.UnitsOfMeasure.CountAsync(x => x.FirmId == firmId, cancellationToken),
        };

        return Result.Success(new DataResetPreview(firm.Name, firm.Code, transactions, masters));
    }

    /// <inheritdoc />
    public async Task<Result<DataResetResult>> ResetAsync(
        DataResetScope scope,
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<DataResetResult>(Error.Forbidden(
                "DataReset.NoFirmSelected", "A firm must be selected to clear its data."));
        }

        if (!Enum.IsDefined(scope))
        {
            return Result.Failure<DataResetResult>(Error.Validation(
                "DataReset.UnknownScope", $"'{scope}' is not something that can be cleared."));
        }

        Firm? firm = await _context.Firms.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == firmId, cancellationToken);

        if (firm is null)
        {
            return Result.Failure<DataResetResult>(Error.NotFound(
                "Firm.NotFound", "The selected firm no longer exists."));
        }

        // Named, so a reset is never one click away from somebody who meant another firm.
        string typed = confirmation?.Trim() ?? string.Empty;

        if (!string.Equals(typed, firm.Name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(typed, firm.Code, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<DataResetResult>(Error.Validation(
                "DataReset.NotConfirmed",
                $"Type the firm's name, '{firm.Name}', to confirm. Nothing was cleared."));
        }

        string[] kept = KeptLedgers(firm);

        string[] statements = scope == DataResetScope.Everything
            ? [.. Transactions, .. Masters]
            : Transactions;

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        Dictionary<string, int> removed = await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            Dictionary<string, int> counts = new(StringComparer.Ordinal);

            foreach (string sql in statements)
            {
                int rows = await _context.Database.ExecuteSqlRawAsync(
                    sql,
                    [
                        new NpgsqlParameter("firm", firmId.Value),
                        new NpgsqlParameter("kept", kept),
                    ],
                    cancellationToken);

                if (rows > 0 && sql.TrimStart().StartsWith("DELETE", StringComparison.Ordinal))
                {
                    string table = TableOf(sql);
                    counts[table] = counts.GetValueOrDefault(table) + rows;
                }
            }

            if (scope == DataResetScope.Everything)
            {
                counts["account_groups"] = await RemoveOwnGroupsAsync(firmId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return counts;
        });

        return Result.Success(new DataResetResult(
            scope,
            removed.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    /// <summary>The ledgers the chart of accounts seeded, which a reset keeps.</summary>
    private static string[] KeptLedgers(Firm firm) =>
    [
        .. ChartOfAccountsTemplate.CommonLedgers.Select(ledger => ledger.Code),
        .. ChartOfAccountsTemplate.TaxLedgersFor(firm.TaxRegime).Select(ledger => ledger.Code),
    ];

    private static string TableOf(string sql)
    {
        string[] words = sql.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

        return words.Length > 2 ? words[2] : sql;
    }

    /// <summary>Removes the groups the firm added, deepest first, keeping the seeded tree.</summary>
    /// <remarks>
    /// Kept by the template's codes as well as the system flag: only the roots carry the
    /// flag, and Sundry Debtors and Sundry Creditors - children - are what customers and
    /// suppliers are created under.
    /// </remarks>
    private async Task<int> RemoveOwnGroupsAsync(FirmId firmId, CancellationToken cancellationToken)
    {
        int total = 0;

        string[] seeded =
        [
            .. ChartOfAccountsTemplate.Roots.Select(group => group.Code),
            .. ChartOfAccountsTemplate.Children.Select(group => group.Code),
        ];

        // A group the firm added can hold another it added, so the leaves go first and the
        // loop repeats until a pass removes nothing.
        while (true)
        {
            int rows = await _context.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM account_groups grp
                WHERE grp.firm_id = @firm AND NOT grp.is_system_group
                  AND NOT (grp.code = ANY(@seeded))
                  AND NOT EXISTS (SELECT 1 FROM account_groups child WHERE child.parent_id = grp.id)
                  AND NOT EXISTS (SELECT 1 FROM ledgers ledger WHERE ledger.account_group_id = grp.id)
                """,
                [new NpgsqlParameter("firm", firmId.Value), new NpgsqlParameter("seeded", seeded)],
                cancellationToken);

            if (rows == 0)
            {
                return total;
            }

            total += rows;
        }
    }
}
