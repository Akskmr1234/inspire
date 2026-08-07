using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>Reads and writes stock documents.</summary>
public sealed class StockDocumentRepository : IStockDocumentRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockDocumentRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockDocumentRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<StockDocument?> FindAsync(
        StockDocumentId id,
        CancellationToken cancellationToken = default) =>
        _context.StockDocuments
            .Include(document => document.Lines)
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    /// <inheritdoc />
    public void Add(StockDocument document) => _context.StockDocuments.Add(document);
}

/// <summary>Reads and writes stock positions.</summary>
public sealed class StockBalanceRepository : IStockBalanceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockBalanceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockBalanceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ProductId, StockBalance>> GetPositionsAsync(
        FirmId firmId,
        WarehouseId warehouseId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<ProductId, StockBalance>();
        }

        // No row lock is taken, and none is needed. Two storekeepers issuing the same
        // product at once both read a quantity of ten and both write eight; the second
        // UPDATE carries the xmin it read, no longer matches, and EF Core raises a
        // concurrency failure that the API turns into a 409. The loser is told to
        // reload rather than quietly overwriting a colleague's issue - which is the
        // outcome pessimistic locking would buy at the cost of holding rows for the
        // length of a transaction that also writes a document and a ledger.
        //
        // Ordering by product is still worth doing: it keeps the UPDATE order stable
        // across concurrent documents, so two of them touching the same two products
        // cannot take PostgreSQL's row locks in opposite orders at write time.
        List<ProductId> ordered = [.. productIds.Distinct().OrderBy(id => id.Value)];

        List<StockBalance> balances = await _context.StockBalances
            .Where(balance =>
                balance.FirmId == firmId
                && balance.WarehouseId == warehouseId
                && ordered.Contains(balance.ProductId))
            .OrderBy(balance => balance.ProductId)
            .ToListAsync(cancellationToken);

        return balances.ToDictionary(balance => balance.ProductId);
    }

    /// <inheritdoc />
    public void Add(StockBalance balance) => _context.StockBalances.Add(balance);
}

/// <summary>Writes and reads back the stock ledger.</summary>
public sealed class StockLedgerRepository : IStockLedgerRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="StockLedgerRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public StockLedgerRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(StockLedgerEntry entry) => _context.StockLedgerEntries.Add(entry);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockLedgerEntry>> ForDocumentAsync(
        StockDocumentId documentId,
        CancellationToken cancellationToken = default) =>
        await _context.StockLedgerEntries
            .Where(entry => entry.DocumentId == documentId)
            .OrderBy(entry => entry.PostedAtUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
}
