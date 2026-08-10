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
            .ThenInclude(line => line.Serials)
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
    public Task<bool> HasStockAsync(
        FirmId firmId,
        ProductId productId,
        CancellationToken cancellationToken = default) =>
        _context.StockBalances
            .AnyAsync(
                balance => balance.FirmId == firmId
                    && balance.ProductId == productId
                    && balance.Quantity != 0m,
                cancellationToken);

    /// <inheritdoc />
    public void Add(StockBalance balance) => _context.StockBalances.Add(balance);
}

/// <summary>Reads and writes batches.</summary>
public sealed class BatchRepository : IBatchRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="BatchRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public BatchRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Batch?> FindAsync(BatchId id, CancellationToken cancellationToken = default) =>
        _context.Batches.FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<BatchId, Batch>> GetManyAsync(
        IReadOnlyCollection<BatchId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<BatchId, Batch>();
        }

        List<BatchId> ordered = [.. ids.Distinct().OrderBy(id => id.Value)];

        List<Batch> batches = await _context.Batches
            .Where(batch => ordered.Contains(batch.Id))
            .ToListAsync(cancellationToken);

        return batches.ToDictionary(batch => batch.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(ProductId Product, string Number), Batch>>
        GetByNumbersAsync(
            FirmId firmId,
            IReadOnlyCollection<(ProductId Product, string Number)> numbers,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        if (numbers.Count == 0)
        {
            return new Dictionary<(ProductId, string), Batch>();
        }

        // Both sides of the pair are filtered in the database and the pairing is
        // matched in memory. A document naming several products and several numbers
        // would otherwise be an OR of tuple comparisons that no provider indexes well,
        // and the two lists are short: they come from the lines of one document.
        List<ProductId> productIds = [.. numbers.Select(pair => pair.Product).Distinct()];
        List<string> batchNumbers = [.. numbers.Select(pair => pair.Number).Distinct()];

        List<Batch> found = await _context.Batches
            .Where(batch =>
                batch.FirmId == firmId
                && productIds.Contains(batch.ProductId)
                && batchNumbers.Contains(batch.Number))
            .ToListAsync(cancellationToken);

        HashSet<(ProductId, string)> wanted = [.. numbers];

        return found
            .Where(batch => wanted.Contains((batch.ProductId, batch.Number)))
            .ToDictionary(batch => (batch.ProductId, batch.Number));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ProductId, int>> GetHighestAutoSequencesAsync(
        FirmId firmId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<ProductId, int>();
        }

        List<ProductId> ordered = [.. productIds.Distinct().OrderBy(id => id.Value)];

        List<KeyValuePair<ProductId, int>> highest = await _context.Batches
            .Where(batch =>
                batch.FirmId == firmId
                && ordered.Contains(batch.ProductId)
                && batch.AutoSequence != null)
            .GroupBy(batch => batch.ProductId)
            .Select(group => new KeyValuePair<ProductId, int>(
                group.Key, group.Max(batch => batch.AutoSequence!.Value)))
            .ToListAsync(cancellationToken);

        return new Dictionary<ProductId, int>(highest);
    }

    /// <inheritdoc />
    public void Add(Batch batch) => _context.Batches.Add(batch);
}

/// <summary>Reads and writes serialised units.</summary>
public sealed class SerialNumberRepository : ISerialNumberRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="SerialNumberRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public SerialNumberRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SerialNumberId, SerialNumber>> GetManyAsync(
        IReadOnlyCollection<SerialNumberId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new Dictionary<SerialNumberId, SerialNumber>();
        }

        List<SerialNumberId> ordered = [.. ids.Distinct().OrderBy(id => id.Value)];

        List<SerialNumber> serials = await _context.SerialNumbers
            .Where(serial => ordered.Contains(serial.Id))
            .ToListAsync(cancellationToken);

        return serials.ToDictionary(serial => serial.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber>>
        GetByNumbersAsync(
            FirmId firmId,
            IReadOnlyCollection<(ProductId Product, string Number)> numbers,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        if (numbers.Count == 0)
        {
            return new Dictionary<(ProductId, string), SerialNumber>();
        }

        // Both sides filtered in the database and the pairing matched in memory, as
        // for batches: an OR of tuple comparisons indexes badly, and the two lists come
        // from the lines of one document.
        List<ProductId> productIds = [.. numbers.Select(pair => pair.Product).Distinct()];
        List<string> serialNumbers = [.. numbers.Select(pair => pair.Number).Distinct()];

        List<SerialNumber> found = await _context.SerialNumbers
            .Where(serial =>
                serial.FirmId == firmId
                && productIds.Contains(serial.ProductId)
                && serialNumbers.Contains(serial.Number))
            .ToListAsync(cancellationToken);

        HashSet<(ProductId, string)> wanted = [.. numbers];

        return found
            .Where(serial => wanted.Contains((serial.ProductId, serial.Number)))
            .ToDictionary(serial => (serial.ProductId, serial.Number));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<SerialNumberId, SerialNumber>> ForDocumentAsync(
        StockDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        List<SerialNumber> serials = await _context.StockDocumentLineSerials
            .Where(link => _context.StockDocumentLines
                .Any(line =>
                    line.Id == link.StockDocumentLineId
                    && line.StockDocumentId == documentId))
            .Join(
                _context.SerialNumbers,
                link => link.SerialNumberId,
                serial => serial.Id,
                (_, serial) => serial)
            .OrderBy(serial => serial.Id)
            .ToListAsync(cancellationToken);

        return serials.ToDictionary(serial => serial.Id);
    }

    /// <inheritdoc />
    public void Add(SerialNumber serial) => _context.SerialNumbers.Add(serial);
}

/// <summary>Reads and writes the position of a batch in a warehouse.</summary>
public sealed class BatchBalanceRepository : IBatchBalanceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="BatchBalanceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public BatchBalanceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<BatchId, BatchBalance>> GetPositionsAsync(
        FirmId firmId,
        WarehouseId warehouseId,
        IReadOnlyCollection<BatchId> batchIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchIds);

        if (batchIds.Count == 0)
        {
            return new Dictionary<BatchId, BatchBalance>();
        }

        // Ordered for the same reason the product positions are: a stable update order
        // across concurrent documents, so two of them touching the same two batches
        // cannot take PostgreSQL's row locks in opposite orders at write time.
        List<BatchId> ordered = [.. batchIds.Distinct().OrderBy(id => id.Value)];

        List<BatchBalance> balances = await _context.BatchBalances
            .Where(balance =>
                balance.FirmId == firmId
                && balance.WarehouseId == warehouseId
                && ordered.Contains(balance.BatchId))
            .OrderBy(balance => balance.BatchId)
            .ToListAsync(cancellationToken);

        return balances.ToDictionary(balance => balance.BatchId);
    }

    /// <inheritdoc />
    public void Add(BatchBalance balance) => _context.BatchBalances.Add(balance);
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

/// <summary>Reads the accounts a firm's stock movements post to.</summary>
public sealed class InventoryAccountMapRepository : IInventoryAccountMapRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="InventoryAccountMapRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public InventoryAccountMapRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<InventoryAccountMap?> FindAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default) =>
        _context.InventoryAccountMaps
            .Include(map => map.Accounts)
            .FirstOrDefaultAsync(map => map.FirmId == firmId, cancellationToken);

    /// <inheritdoc />
    public void Add(InventoryAccountMap map) => _context.InventoryAccountMaps.Add(map);
}
