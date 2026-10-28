using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>The PostgreSQL implementation of <see cref="IPrimaryParties"/>.</summary>
/// <remarks>
/// <para>
/// Made the first time the list is read rather than when the firm is set up, so it
/// reaches every firm - those set up before there were Primary parties, and one whose
/// masters were cleared - without touching the set-up itself. That is the same way the
/// Primary category and brand arrive.
/// </para>
/// <para>
/// Only for a firm with no party of the kind at all: one that has entered its own
/// customers is not given another. One statement, and a filtered unique index allows
/// one Primary party of each kind per firm, so two lists read at the same moment cannot
/// add two; the second finds nothing to do.
/// </para>
/// </remarks>
public sealed class PrimaryParties : IPrimaryParties
{
    // The groups the standard chart files customers and suppliers under - the same
    // ones a customer or supplier created without a group goes to.
    private const string DebtorsGroupCode = "1200";
    private const string CreditorsGroupCode = "2200";

    private const string Sql = """
        INSERT INTO ledgers (
            id, tenant_id, firm_id, account_group_id, code, name, name_arabic, kind, currency,
            opening_balance, opening_balance_side, is_active, is_bill_wise, is_primary,
            sends_sms, is_deleted, created_at_utc, created_by)
        SELECT gen_random_uuid(), firm.tenant_id, firm.id, grp.id, @code, @name, @nameArabic,
               @kind, firm.base_currency, 0, @side, true, true, true, false, false, now(),
               '00000000-0000-0000-0000-00000000513e'::uuid
        FROM firms firm
        JOIN account_groups grp
          ON grp.firm_id = firm.id AND grp.code = @group AND grp.is_deleted = false
        WHERE firm.id = @firm
          AND NOT EXISTS (
              SELECT 1 FROM ledgers
              WHERE firm_id = @firm AND kind = @kind AND is_deleted = false)
        ON CONFLICT DO NOTHING
        """;

    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PrimaryParties"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PrimaryParties(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task EnsureAsync(FirmId firmId, LedgerKind kind, CancellationToken cancellationToken = default)
    {
        bool customer = kind == LedgerKind.Customer;

        if (!customer && kind != LedgerKind.Supplier)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only customers and suppliers have a Primary record.");
        }

        await _context.Database.ExecuteSqlRawAsync(
            Sql,
            [
                new NpgsqlParameter("firm", firmId.Value),
                new NpgsqlParameter("kind", (int)kind),
                new NpgsqlParameter("group", customer ? DebtorsGroupCode : CreditorsGroupCode),
                new NpgsqlParameter("code", customer ? Ledger.PrimaryCustomerCode : Ledger.PrimarySupplierCode),
                new NpgsqlParameter("name", customer ? "Primary Customer" : "Primary Supplier"),
                new NpgsqlParameter("nameArabic", customer ? "عميل رئيسي" : "مورد رئيسي"),

                // What a customer owes is a debit; what is owed to a supplier, a credit.
                new NpgsqlParameter("side", (int)(customer ? EntrySide.Debit : EntrySide.Credit)),
            ],
            cancellationToken);
    }
}
