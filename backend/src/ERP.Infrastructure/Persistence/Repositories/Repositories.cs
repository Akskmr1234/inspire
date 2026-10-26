using ERP.Application;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Platform.Dashboards;
using ERP.Application.Platform.Grids;
using ERP.Application.Platform.Settings;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.Infrastructure.Auditing;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>The EF Core unit of work.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ErpDbContext _context;
    private readonly AuditTrail? _audit;

    /// <summary>Initialises a new instance of the <see cref="UnitOfWork"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="audit">The audit trail, written once a transaction commits.</param>
    public UnitOfWork(ErpDbContext context, AuditTrail? audit = null)
    {
        _context = context;
        _audit = audit;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Unwraps <see cref="TransactionRollbackException"/> so a handler that returned
    /// a failure gets that failure back to the caller, having rolled the transaction
    /// back on the way. Without this the caller would see a 500 for what is really
    /// an ordinary business-rule rejection.
    /// </remarks>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Already inside a transaction - a nested handler, or a test controlling its
        // own boundary. Joining rather than nesting keeps one atomic unit.
        if (_context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        // The execution strategy owns the retry loop, and a retry has to replay the
        // whole transaction rather than part of it - which is why the transaction is
        // opened inside the strategy and not around it.
        return await strategy.ExecuteAsync(async () =>
        {
            // A retried attempt starts again from nothing, its trail included.
            _audit?.Discard();

            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                TResult result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Only now did the changes happen, so only now are they written down.
                if (_audit is not null)
                {
                    await _audit.FlushAsync(cancellationToken);
                }

                return result;
            }
            catch (TransactionRollbackException rollback)
            {
                await transaction.RollbackAsync(cancellationToken);
                _audit?.Discard();

                return (TResult)rollback.Response;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _audit?.Discard();
                throw;
            }
        });
    }
}

/// <summary>The EF Core voucher repository.</summary>
public sealed class VoucherRepository : IVoucherRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="VoucherRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public VoucherRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Voucher?> FindAsync(VoucherId id, CancellationToken cancellationToken = default) =>
        _context.Vouchers
            .Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(Voucher voucher) => _context.Vouchers.Add(voucher);

    /// <inheritdoc />
    public async Task<string?> FindRaisingDocumentAsync(
        VoucherId id,
        CancellationToken cancellationToken = default)
    {
        VoucherId? journal = id;

        return await _context.SalesInvoices
                .Where(invoice => invoice.JournalVoucherId == journal)
                .Select(invoice => invoice.Number)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _context.PurchaseInvoices
                .Where(invoice => invoice.JournalVoucherId == journal)
                .Select(invoice => invoice.Number)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _context.StockDocuments
                .Where(document => document.JournalVoucherId == journal)
                .Select(document => document.Number)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _context.Cheques
                .Where(cheque => cheque.ClearingVoucherId == journal
                    || cheque.ReversalVoucherId == journal)
                .Select(cheque => "cheque " + cheque.ChequeNumber)
                .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>The EF Core ledger repository.</summary>
public sealed class LedgerRepository : ILedgerRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="LedgerRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public LedgerRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Ledger?> FindAsync(LedgerId id, CancellationToken cancellationToken = default) =>
        _context.Ledgers.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<LedgerId, Ledger>> GetManyAsync(
        IEnumerable<LedgerId> ids,
        CancellationToken cancellationToken = default)
    {
        List<LedgerId> requested = [.. ids];

        List<Ledger> found = await _context.Ledgers
            .Where(l => requested.Contains(l.Id))
            .ToListAsync(cancellationToken);

        return found.ToDictionary(l => l.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Ledger Ledger, AccountGroup Group)>> ListWithGroupAsync(
        FirmId firmId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Ledgers
            .Where(l => l.FirmId == firmId && (!activeOnly || l.IsActive))
            .Join(
                _context.AccountGroups,
                ledger => ledger.AccountGroupId,
                group => group.Id,
                (ledger, group) => new { ledger, group })
            .OrderBy(x => x.group.Code)
            .ThenBy(x => x.ledger.Code)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => (x.ledger, x.group))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Ledger>> ListByKindAsync(
        FirmId firmId,
        LedgerKind kind,
        string? search = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Ledger> query = _context.Ledgers
            .Where(l => l.FirmId == firmId && l.Kind == kind && (!activeOnly || l.IsActive));

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();

            query = query.Where(l =>
                EF.Functions.ILike(l.Code, $"%{term}%")
                || EF.Functions.ILike(l.Name, $"%{term}%")
                || (l.MobileNumber != null && EF.Functions.ILike(l.MobileNumber, $"%{term}%")));
        }

        return await query.OrderBy(l => l.Name).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<AccountGroup?> FindGroupAsync(
        AccountGroupId id,
        CancellationToken cancellationToken = default) =>
        _context.AccountGroups.FirstOrDefaultAsync(group => group.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<AccountGroup?> FindGroupByCodeAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) =>
        _context.AccountGroups.FirstOrDefaultAsync(
            group => group.FirmId == firmId && group.Code == code, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsCodeInUseAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) =>
        _context.Ledgers.AnyAsync(
            l => l.FirmId == firmId && l.Code == code, cancellationToken);

    /// <inheritdoc />
    public void Add(Ledger ledger) => _context.Ledgers.Add(ledger);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountGroup>> ListGroupsAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        await _context.AccountGroups
            .Where(group => group.FirmId == firmId)
            .OrderBy(group => group.Code)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsGroupCodeInUseAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) =>
        _context.AccountGroups.AnyAsync(
            group => group.FirmId == firmId && group.Code == code, cancellationToken);

    /// <inheritdoc />
    public void AddGroup(AccountGroup group) => _context.AccountGroups.Add(group);
}

/// <summary>The EF Core additional-charge repository.</summary>
public sealed class AdditionalLedgerRepository : IAdditionalLedgerRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="AdditionalLedgerRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public AdditionalLedgerRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdditionalLedger>> ListForDocumentAsync(
        FirmId firmId,
        ChargeableDocument document,
        CancellationToken cancellationToken = default) =>
        await _context.AdditionalLedgers
            .Where(charge => charge.FirmId == firmId && charge.Document == document)
            .OrderBy(charge => charge.DisplayOrder)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(AdditionalLedger charge) => _context.AdditionalLedgers.Add(charge);
}

/// <summary>The EF Core sales invoice repository.</summary>
public sealed class SalesInvoiceRepository : ISalesInvoiceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesInvoiceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesInvoiceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<SalesInvoice?> FindAsync(
        SalesInvoiceId id,
        CancellationToken cancellationToken = default) =>
        _context.SalesInvoices
            .Include(invoice => invoice.Lines).ThenInclude(line => line.Components)
            .Include(invoice => invoice.Lines).ThenInclude(line => line.Serials)
            .Include(invoice => invoice.Charges)
            .FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<string?> FindBillingDeliveryNoteAsync(
        StockDocumentId deliveryNoteId,
        SalesInvoiceId? except,
        CancellationToken cancellationToken = default) =>
        _context.SalesInvoices
            .Where(invoice => invoice.DeliveryNoteId == deliveryNoteId
                && invoice.Status != SalesInvoiceStatus.Cancelled
                && (except == null || invoice.Id != except))
            .Select(invoice => invoice.Number)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(SalesInvoice invoice) => _context.SalesInvoices.Add(invoice);
}

/// <summary>The EF Core sales order repository.</summary>
public sealed class SalesOrderRepository : ISalesOrderRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SalesOrderRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SalesOrderRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<SalesOrder?> FindAsync(
        SalesOrderId id,
        CancellationToken cancellationToken = default) =>
        _context.SalesOrders
            .Include(order => order.Lines).ThenInclude(line => line.Components)
            .Include(order => order.Charges)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(SalesOrder order) => _context.SalesOrders.Add(order);
}

/// <summary>The EF Core purchase order repository.</summary>
public sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PurchaseOrderRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PurchaseOrderRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<PurchaseOrder?> FindAsync(
        PurchaseOrderId id,
        CancellationToken cancellationToken = default) =>
        _context.PurchaseOrders
            .Include(order => order.Lines).ThenInclude(line => line.Components)
            .Include(order => order.Charges)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(PurchaseOrder order) => _context.PurchaseOrders.Add(order);
}

/// <summary>The EF Core purchase invoice repository.</summary>
public sealed class PurchaseInvoiceRepository : IPurchaseInvoiceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PurchaseInvoiceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PurchaseInvoiceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<PurchaseInvoice?> FindAsync(
        PurchaseInvoiceId id,
        CancellationToken cancellationToken = default) =>
        _context.PurchaseInvoices
            .Include(invoice => invoice.Lines).ThenInclude(line => line.Components)
            .Include(invoice => invoice.Lines).ThenInclude(line => line.Serials)
            .Include(invoice => invoice.Charges)
            .FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<string?> FindBillingReceiptNoteAsync(
        StockDocumentId receiptNoteId,
        PurchaseInvoiceId? except,
        CancellationToken cancellationToken = default) =>
        _context.PurchaseInvoices
            .Where(invoice => invoice.ReceiptNoteId == receiptNoteId
                && invoice.Status != PurchaseInvoiceStatus.Cancelled
                && (except == null || invoice.Id != except))
            .Select(invoice => invoice.Number)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(PurchaseInvoice invoice) => _context.PurchaseInvoices.Add(invoice);

    /// <inheritdoc />
    public Task<bool> IsSupplierInvoiceNumberInUseAsync(
        FirmId firmId,
        LedgerId supplierLedgerId,
        string supplierInvoiceNumber,
        PurchaseInvoiceId? except = null,
        CancellationToken cancellationToken = default) =>
        _context.PurchaseInvoices.AnyAsync(
            invoice =>
                invoice.FirmId == firmId
                && invoice.SupplierLedgerId == supplierLedgerId
                && invoice.SupplierInvoiceNumber == supplierInvoiceNumber
                && invoice.Status != PurchaseInvoiceStatus.Cancelled
                && (except == null || invoice.Id != except),
            cancellationToken);
}

/// <summary>The EF Core tax account map repository.</summary>
public sealed class TaxAccountMapRepository : ITaxAccountMapRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="TaxAccountMapRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public TaxAccountMapRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<TaxAccountMap?> FindAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        _context.TaxAccountMaps
            .Include(map => map.Accounts)
            .FirstOrDefaultAsync(map => map.FirmId == firmId, cancellationToken);

    /// <inheritdoc />
    public void Add(TaxAccountMap map) => _context.TaxAccountMaps.Add(map);
}

/// <summary>The EF Core bill repository.</summary>
public sealed class BillRepository : IBillRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="BillRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public BillRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<BillId, Bill>> GetManyAsync(
        IEnumerable<BillId> ids,
        CancellationToken cancellationToken = default)
    {
        List<BillId> requested = [.. ids];

        List<Bill> found = await _context.Bills
            .Include(b => b.Allocations)
            .Where(b => requested.Contains(b.Id))
            .ToListAsync(cancellationToken);

        return found.ToDictionary(b => b.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindExistingReferencesAsync(
        FirmId firmId,
        LedgerId ledgerId,
        IEnumerable<string> billNumbers,
        CancellationToken cancellationToken = default)
    {
        List<string> requested = [.. billNumbers];

        if (requested.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> existing = await _context.Bills
            .Where(b =>
                b.FirmId == firmId
                && b.LedgerId == ledgerId
                && b.Status != BillStatus.Cancelled
                && requested.Contains(b.BillNumber))
            .Select(b => b.BillNumber)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bill>> FindRaisedByAsync(
        VoucherId voucherId,
        CancellationToken cancellationToken = default) =>
        await _context.Bills
            .Include(b => b.Allocations)
            .Where(b => b.OriginVoucherId == voucherId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bill>> FindAllocatedByAsync(
        VoucherId voucherId,
        CancellationToken cancellationToken = default)
    {
        // Through the allocations table rather than the bills' navigation, so the
        // filter runs in the database on the indexed voucher column instead of
        // loading every bill's history to sift it in memory.
        List<BillId> billIds = await _context.BillAllocations
            .Where(a => a.VoucherId == voucherId)
            .Select(a => a.BillId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (billIds.Count == 0)
        {
            return [];
        }

        return await _context.Bills
            .Include(b => b.Allocations)
            .Where(b => billIds.Contains(b.Id))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Bill bill) => _context.Bills.Add(bill);
}

/// <summary>The EF Core cheque repository.</summary>
public sealed class ChequeRepository : IChequeRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="ChequeRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ChequeRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Cheque?> FindAsync(ChequeId id, CancellationToken cancellationToken = default) =>
        _context.Cheques.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindLiveNumbersAsync(
        FirmId firmId,
        LedgerId partyLedgerId,
        IEnumerable<string> chequeNumbers,
        CancellationToken cancellationToken = default)
    {
        List<string> requested = [.. chequeNumbers];

        if (requested.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> live = await _context.Cheques
            .Where(c =>
                c.FirmId == firmId
                && c.PartyLedgerId == partyLedgerId
                && (c.Status == ChequeStatus.Pending || c.Status == ChequeStatus.Deposited)
                && requested.Contains(c.ChequeNumber))
            .Select(c => c.ChequeNumber)
            .ToListAsync(cancellationToken);

        return live.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindLiveIssuedNumbersAsync(
        FirmId firmId,
        LedgerId bankLedgerId,
        IReadOnlyCollection<string> chequeNumbers,
        CancellationToken cancellationToken = default)
    {
        if (chequeNumbers.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        List<string> live = await _context.Cheques
            .Where(c =>
                c.FirmId == firmId
                && c.Direction == ChequeDirection.Issued
                && c.BankLedgerId == bankLedgerId
                && (c.Status == ChequeStatus.Pending || c.Status == ChequeStatus.Deposited)
                && chequeNumbers.Contains(c.ChequeNumber))
            .Select(c => c.ChequeNumber)
            .ToListAsync(cancellationToken);

        return live.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public void Add(Cheque cheque) => _context.Cheques.Add(cheque);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Cheque>> FindRecordedByAsync(
        VoucherId voucherId,
        CancellationToken cancellationToken = default) =>
        await _context.Cheques
            .Where(c => c.OriginVoucherId == voucherId)
            .ToListAsync(cancellationToken);
}

/// <summary>The EF Core firm repository.</summary>
public sealed class FirmRepository : IFirmRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="FirmRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public FirmRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Firm?> FindAsync(FirmId id, CancellationToken cancellationToken = default) =>
        _context.Firms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
}

/// <summary>The EF Core financial-year repository.</summary>
public sealed class FinancialYearRepository : IFinancialYearRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="FinancialYearRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public FinancialYearRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<FinancialYear?> FindContainingAsync(
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _context.FinancialYears
            .Where(y => y.FirmId == firmId && y.StartDate <= date && y.EndDate >= date)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FinancialYear>> ListAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        await _context.FinancialYears
            .Where(y => y.FirmId == firmId)
            .OrderBy(y => y.StartDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<FinancialYear?> FindAsync(
        FinancialYearId id,
        CancellationToken cancellationToken = default) =>
        _context.FinancialYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Every document that belongs to a year carries it, so the question is the same for
    /// each kind. Cancelled documents count too: they are kept as the record of what was
    /// entered, and a year that moved out from under one would leave it in no period.
    /// </remarks>
    public async Task<int> CountDocumentsOutsideAsync(
        FinancialYearId id,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        int vouchers = await _context.Vouchers.CountAsync(
            v => v.FinancialYearId == id && (v.Date < startDate || v.Date > endDate),
            cancellationToken);
        int sales = await _context.SalesInvoices.CountAsync(
            i => i.FinancialYearId == id && (i.Date < startDate || i.Date > endDate),
            cancellationToken);
        int purchases = await _context.PurchaseInvoices.CountAsync(
            i => i.FinancialYearId == id && (i.Date < startDate || i.Date > endDate),
            cancellationToken);
        int stock = await _context.StockDocuments.CountAsync(
            d => d.FinancialYearId == id && (d.Date < startDate || d.Date > endDate),
            cancellationToken);
        int orders = await _context.SalesOrders.CountAsync(
            o => o.FinancialYearId == id && (o.Date < startDate || o.Date > endDate),
            cancellationToken)
            + await _context.PurchaseOrders.CountAsync(
                o => o.FinancialYearId == id && (o.Date < startDate || o.Date > endDate),
                cancellationToken);

        return vouchers + sales + purchases + stock + orders;
    }

    /// <inheritdoc />
    public void Add(FinancialYear year) => _context.FinancialYears.Add(year);
}

/// <summary>The EF Core numbering-series repository.</summary>
public sealed class NumberingSeriesRepository : INumberingSeriesRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="NumberingSeriesRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public NumberingSeriesRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// Resolves most-specific-first - branch and year, then branch, then year, then
    /// firm-wide - so an administrator can override numbering for one branch without
    /// redefining it everywhere.
    /// <para>
    /// Ordering is done in memory over at most four candidate rows. Expressing the
    /// precedence in SQL would need a CASE ranking that reads far worse for no
    /// measurable gain at this size.
    /// </para>
    /// <para>
    /// A series this transaction has created but not yet saved counts as found. One
    /// posting can reserve two numbers from the same sequence - a purchase raises a
    /// journal for its stock and another for the invoice, both from the firm's journal
    /// series - and on the first document of a firm's life neither reservation would see
    /// the other's row. Both would create the series, and the save would fail on a unique
    /// index with a message about an index rather than about numbering. Reading the change
    /// tracker first is what makes the second reservation take a number from the first's
    /// series instead of opening a rival one.
    /// </para>
    /// </remarks>
    public async Task<NumberingSeries?> FindForUpdateAsync(
        string documentType,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        CancellationToken cancellationToken = default)
    {
        List<NumberingSeries> candidates = await _context.NumberingSeries
            .Where(s =>
                s.DocumentType == documentType
                && s.FirmId == firmId
                && s.IsActive
                && (s.BranchId == null || s.BranchId == branchId)
                && (s.FinancialYearId == null || s.FinancialYearId == financialYearId))
            .ToListAsync(cancellationToken);

        // Anything this transaction has added is already tracked, and a query cannot see
        // it because it has not been written yet.
        candidates.AddRange(_context.NumberingSeries.Local
            .Where(s =>
                !candidates.Contains(s)
                && string.Equals(s.DocumentType, documentType, StringComparison.Ordinal)
                && s.FirmId == firmId
                && s.IsActive
                && (s.BranchId is null || s.BranchId == branchId)
                && (s.FinancialYearId is null || s.FinancialYearId == financialYearId)));

        return candidates
            .OrderByDescending(s => s.BranchId is not null)
            .ThenByDescending(s => s.FinancialYearId is not null)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public void Add(NumberingSeries series) => _context.NumberingSeries.Add(series);
}

/// <summary>The EF Core master code sequence repository.</summary>
public sealed class MasterCodeSequenceRepository : IMasterCodeSequenceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="MasterCodeSequenceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public MasterCodeSequenceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// The change tracker first, for the reason <see cref="NumberingSeriesRepository"/>
    /// gives: two records numbered in one unit of work, on a firm's first day, would
    /// otherwise each start a sequence of their own and the save would fail on the
    /// unique index.
    /// </remarks>
    public async Task<MasterCodeSequence?> FindAsync(
        FirmId firmId,
        string master,
        CancellationToken cancellationToken = default) =>
        _context.MasterCodeSequences.Local.FirstOrDefault(sequence =>
            sequence.FirmId == firmId && string.Equals(sequence.Master, master, StringComparison.Ordinal))
        ?? await _context.MasterCodeSequences.FirstOrDefaultAsync(
            sequence => sequence.FirmId == firmId && sequence.Master == master, cancellationToken);

    /// <inheritdoc />
    public void Add(MasterCodeSequence sequence) => _context.MasterCodeSequences.Add(sequence);
}

/// <summary>Reads and writes menu entries.</summary>
public sealed class MenuItemRepository : IMenuItemRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="MenuItemRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public MenuItemRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<MenuItem?> FindAsync(
        MenuItemId id,
        CancellationToken cancellationToken = default) =>
        _context.MenuItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default) =>
        _context.MenuItems.AnyAsync(
            item => item.FirmId == firmId && item.Code == code, cancellationToken);

    /// <inheritdoc />
    public Task<int> CountChildrenAsync(
        MenuItemId id,
        CancellationToken cancellationToken = default) =>
        _context.MenuItems.CountAsync(item => item.ParentId == id, cancellationToken);

    /// <inheritdoc />
    public void Add(MenuItem item) => _context.MenuItems.Add(item);

    /// <inheritdoc />
    public void Remove(MenuItem item) => _context.MenuItems.Remove(item);
}

/// <summary>Reads and writes saved grid layouts.</summary>
public sealed class GridLayoutRepository : IGridLayoutRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="GridLayoutRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public GridLayoutRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<GridLayout?> FindAsync(
        UserId userId,
        string gridKey,
        CancellationToken cancellationToken = default) =>
        _context.GridLayouts.FirstOrDefaultAsync(
            layout => layout.UserId == userId && layout.GridKey == gridKey,
            cancellationToken);

    /// <inheritdoc />
    public void Add(GridLayout layout) => _context.GridLayouts.Add(layout);

    /// <inheritdoc />
    public void Remove(GridLayout layout) => _context.GridLayouts.Remove(layout);
}

/// <summary>EF Core implementation of <see cref="IFirmSettingRepository"/>.</summary>
public sealed class FirmSettingRepository : IFirmSettingRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="FirmSettingRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public FirmSettingRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<FirmSetting?> FindAsync(
        FirmId firmId,
        string key,
        CancellationToken cancellationToken = default) =>
        _context.FirmSettings.FirstOrDefaultAsync(
            setting => setting.FirmId == firmId && setting.Key == key,
            cancellationToken);

    /// <inheritdoc />
    public void Add(FirmSetting setting) => _context.FirmSettings.Add(setting);
}

/// <summary>Reads and writes dashboards.</summary>
public sealed class DashboardRepository : IDashboardRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="DashboardRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public DashboardRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Dashboard?> FindAsync(
        DashboardId id,
        CancellationToken cancellationToken = default) =>
        _context.Dashboards
            .Include(dashboard => dashboard.Widgets)
            .FirstOrDefaultAsync(dashboard => dashboard.Id == id, cancellationToken);
}
