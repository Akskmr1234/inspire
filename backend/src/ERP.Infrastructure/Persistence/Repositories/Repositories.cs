using ERP.Application;
using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>The EF Core unit of work.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="UnitOfWork"/> class.</summary>
    /// <param name="context">The database context.</param>
    public UnitOfWork(ErpDbContext context) => _context = context;

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
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                TResult result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch (TransactionRollbackException rollback)
            {
                await transaction.RollbackAsync(cancellationToken);

                return (TResult)rollback.Response;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
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

        return candidates
            .OrderByDescending(s => s.BranchId is not null)
            .ThenByDescending(s => s.FinancialYearId is not null)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public void Add(NumberingSeries series) => _context.NumberingSeries.Add(series);
}
