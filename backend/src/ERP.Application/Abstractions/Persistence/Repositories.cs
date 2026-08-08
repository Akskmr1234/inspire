using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Platform;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Persistence;

/// <summary>
/// Commits the work accumulated in the current unit of work, and runs a group of
/// changes as one transaction.
/// </summary>
/// <remarks>
/// Declared here rather than exposing a <c>DbContext</c> so the Application layer
/// stays free of EF Core. That is not architectural purity for its own sake: it is
/// what stops a use case reaching for <c>ExecuteDelete</c> or an
/// <c>IgnoreQueryFilters</c> that would bypass tenant isolation.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists all pending changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs work inside a single database transaction.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="operation">The work to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation's result.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes vouchers.</summary>
public interface IVoucherRepository
{
    /// <summary>Finds a voucher and its lines.</summary>
    /// <param name="id">The voucher.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The voucher, or <see langword="null"/>.</returns>
    Task<Voucher?> FindAsync(VoucherId id, CancellationToken cancellationToken = default);

    /// <summary>Adds a voucher.</summary>
    /// <param name="voucher">The voucher to add.</param>
    void Add(Voucher voucher);
}

/// <summary>Reads ledgers.</summary>
public interface ILedgerRepository
{
    /// <summary>Finds a ledger.</summary>
    /// <param name="id">The ledger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledger, or <see langword="null"/>.</returns>
    Task<Ledger?> FindAsync(LedgerId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads several ledgers at once, keyed by identifier.
    /// </summary>
    /// <param name="ids">The ledgers to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledgers found, keyed by identifier.</returns>
    /// <remarks>
    /// A voucher validates every line's ledger. Loading them one at a time would
    /// issue a query per line, which for a fifty-line journal is fifty round trips.
    /// </remarks>
    Task<IReadOnlyDictionary<LedgerId, Ledger>> GetManyAsync(
        IEnumerable<LedgerId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a firm's ledgers with their account groups, for lookup.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="activeOnly">Whether to exclude deactivated ledgers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledgers, each paired with its group, ordered by group then code.</returns>
    /// <remarks>
    /// Returns the group alongside each ledger because every lookup that uses this
    /// shows it - a voucher entry grid needs to distinguish "Sales Account" under
    /// Sales Accounts from a similarly-named ledger elsewhere in the chart.
    /// </remarks>
    Task<IReadOnlyList<(Ledger Ledger, AccountGroup Group)>> ListWithGroupAsync(
        FirmId firmId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes bills.</summary>
public interface IBillRepository
{
    /// <summary>Loads several bills at once, keyed by identifier.</summary>
    /// <param name="ids">The bills to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bills found, with their allocations, keyed by identifier.</returns>
    /// <remarks>
    /// Allocations are loaded with the bill because settling one appends to them and
    /// re-derives the status from the total. A bill loaded without them would
    /// compute its outstanding amount from an empty collection and let a settled
    /// bill be paid twice.
    /// </remarks>
    Task<IReadOnlyDictionary<BillId, Bill>> GetManyAsync(
        IEnumerable<BillId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a party already has a bill under a reference.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="ledgerId">The party.</param>
    /// <param name="billNumbers">The references to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Those of the supplied references that are already in use.</returns>
    /// <remarks>
    /// A unique index already forbids the duplicate. Checking first turns what would
    /// be a constraint violation surfacing as a 500 into a message naming the
    /// reference that clashed - which is the difference between an operator fixing
    /// their own typo and raising a support ticket.
    /// </remarks>
    Task<IReadOnlySet<string>> FindExistingReferencesAsync(
        FirmId firmId,
        LedgerId ledgerId,
        IEnumerable<string> billNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Loads every bill a voucher allocated against.</summary>
    /// <param name="voucherId">The settling voucher.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bills that voucher settled, with their allocations.</returns>
    /// <remarks>
    /// Wanted when a settlement has to be undone - a cheque that bounces, or a
    /// receipt that is cancelled. The voucher knows what it paid; the bills know
    /// what they were paid by, and this is the direction the reversal needs.
    /// </remarks>
    Task<IReadOnlyList<Bill>> FindAllocatedByAsync(
        VoucherId voucherId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a bill.</summary>
    /// <param name="bill">The bill to add.</param>
    void Add(Bill bill);
}

/// <summary>Reads and writes cheques.</summary>
public interface IChequeRepository
{
    /// <summary>Finds a cheque.</summary>
    /// <param name="id">The cheque.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cheque, or <see langword="null"/>.</returns>
    Task<Cheque?> FindAsync(ChequeId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines which of a set of cheque numbers a party already has live.
    /// </summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="partyLedgerId">The party.</param>
    /// <param name="chequeNumbers">The numbers to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Those of the supplied numbers already on an open cheque.</returns>
    /// <remarks>
    /// Only open cheques count, matching the filtered unique index. A cheque that
    /// bounced is re-presented under the same number, so a check over all history
    /// would refuse the very thing that happens next.
    /// </remarks>
    Task<IReadOnlySet<string>> FindLiveNumbersAsync(
        FirmId firmId,
        LedgerId partyLedgerId,
        IEnumerable<string> chequeNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a cheque.</summary>
    /// <param name="cheque">The cheque to add.</param>
    void Add(Cheque cheque);
}

/// <summary>Reads firms.</summary>
public interface IFirmRepository
{
    /// <summary>Finds a firm.</summary>
    /// <param name="id">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The firm, or <see langword="null"/>.</returns>
    Task<Firm?> FindAsync(FirmId id, CancellationToken cancellationToken = default);
}

/// <summary>Reads financial years.</summary>
public interface IFinancialYearRepository
{
    /// <summary>Finds the financial year containing a date.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="date">The document date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year, or <see langword="null"/> when the date falls outside every year.</returns>
    Task<FinancialYear?> FindContainingAsync(
        FirmId firmId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and creates numbering series.</summary>
public interface INumberingSeriesRepository
{
    /// <summary>
    /// Loads the series governing a document type for a branch and year, taking a
    /// database lock so it can be safely advanced.
    /// </summary>
    /// <param name="documentType">The document type, from <see cref="DocumentTypes"/>.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="financialYearId">The financial year.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The most specific matching series, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Resolution is most-specific-first: a series scoped to both this branch and
    /// this year wins over one scoped to the branch alone, which wins over a
    /// firm-wide series. That is what lets an administrator override numbering for
    /// one branch without redefining it for all of them.
    /// </para>
    /// <para>
    /// The implementation takes a row lock, so two concurrent postings queue rather
    /// than both reading the same next number. The unique index on the document
    /// number is the backstop if that ever fails.
    /// </para>
    /// </remarks>
    Task<NumberingSeries?> FindForUpdateAsync(
        string documentType,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a series.</summary>
    /// <param name="series">The series to add.</param>
    void Add(NumberingSeries series);
}

/// <summary>Reads and writes menu entries.</summary>
public interface IMenuItemRepository
{
    /// <summary>Finds a menu entry.</summary>
    /// <param name="id">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    Task<MenuItem?> FindAsync(MenuItemId id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a firm already uses a menu code.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the code is taken.</returns>
    /// <remarks>
    /// Checked before insert so a duplicate is refused with a message naming the code,
    /// rather than surfacing as a unique-index violation the caller cannot interpret.
    /// The index is still the backstop against two administrators racing.
    /// </remarks>
    Task<bool> CodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the entries sitting directly beneath one.</summary>
    /// <param name="id">The parent entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many entries name it as their parent.</returns>
    /// <remarks>
    /// Deleting a heading must not silently take a subtree of screens with it, so the
    /// caller is refused until the children have been moved. The foreign key restricts
    /// the delete regardless; this turns that into an error somebody can act on.
    /// </remarks>
    Task<int> CountChildrenAsync(
        MenuItemId id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a menu entry.</summary>
    /// <param name="item">The entry to add.</param>
    void Add(MenuItem item);

    /// <summary>Removes a menu entry.</summary>
    /// <param name="item">The entry to remove.</param>
    void Remove(MenuItem item);
}

/// <summary>Reads and writes the inventory masters.</summary>
/// <remarks>
/// One repository for four aggregates, which is unusual here and deliberate. They are
/// edited from one screen, share a shape - a code unique within the firm, a name, an
/// active flag - and every operation on them is the same three questions: does this
/// code exist, fetch this one, add this one. Four near-identical repositories would be
/// four places to fix the same thing.
/// </remarks>
public interface IInventoryMasterRepository
{
    /// <summary>Loads several units of measurement at once.</summary>
    /// <param name="ids">The units.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units that exist, by identifier.</returns>
    /// <remarks>
    /// A stock document needs both the unit each line was entered in and each
    /// product's own stock unit, to convert between them. Fetched together because
    /// they overlap heavily - most lines are entered in the stock unit - and a query
    /// per line would be mostly the same query.
    /// </remarks>
    Task<IReadOnlyDictionary<UnitOfMeasureId, UnitOfMeasure>> GetUnitsAsync(
        IReadOnlyCollection<UnitOfMeasureId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a unit of measurement.</summary>
    /// <param name="id">The unit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The unit, or <see langword="null"/>.</returns>
    Task<UnitOfMeasure?> FindUnitAsync(
        UnitOfMeasureId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a category.</summary>
    /// <param name="id">The category.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The category, or <see langword="null"/>.</returns>
    Task<Category?> FindCategoryAsync(
        CategoryId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a brand.</summary>
    /// <param name="id">The brand.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The brand, or <see langword="null"/>.</returns>
    Task<Brand?> FindBrandAsync(BrandId id, CancellationToken cancellationToken = default);

    /// <summary>Finds a warehouse.</summary>
    /// <param name="id">The warehouse.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The warehouse, or <see langword="null"/>.</returns>
    Task<Warehouse?> FindWarehouseAsync(
        WarehouseId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the warehouse new documents currently default to.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The default warehouse, or <see langword="null"/> when none is set.</returns>
    /// <remarks>
    /// Needed because promoting one warehouse has to demote the other in the same
    /// transaction. The filtered unique index would otherwise reject the second write,
    /// correctly but unhelpfully.
    /// </remarks>
    Task<Warehouse?> FindDefaultWarehouseAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);

    /// <summary>Determines whether a code is already used by a master of one kind.</summary>
    /// <param name="kind">Which master to look in.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the code is taken.</returns>
    /// <remarks>
    /// Checked before insert so a duplicate is refused with a message naming the code
    /// rather than surfacing as a unique-index violation nobody can interpret. The
    /// index remains the backstop against two people saving at once.
    /// </remarks>
    Task<bool> CodeExistsAsync(
        InventoryMasterKind kind,
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a unit of measurement.</summary>
    /// <param name="unit">The unit to add.</param>
    void Add(UnitOfMeasure unit);

    /// <summary>Adds a category.</summary>
    /// <param name="category">The category to add.</param>
    void Add(Category category);

    /// <summary>Adds a brand.</summary>
    /// <param name="brand">The brand to add.</param>
    void Add(Brand brand);

    /// <summary>Adds a warehouse.</summary>
    /// <param name="warehouse">The warehouse to add.</param>
    void Add(Warehouse warehouse);
}

/// <summary>Which inventory master a code is being checked against.</summary>
public enum InventoryMasterKind
{
    /// <summary>A unit of measurement.</summary>
    UnitOfMeasure = 1,

    /// <summary>A product category or sub-class.</summary>
    Category = 2,

    /// <summary>A brand.</summary>
    Brand = 3,

    /// <summary>A warehouse.</summary>
    Warehouse = 4,
}

/// <summary>Reads and writes the product master.</summary>
public interface IProductRepository
{
    /// <summary>Finds a product with its barcodes.</summary>
    /// <param name="id">The product.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The product, or <see langword="null"/>.</returns>
    Task<Product?> FindAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a firm already uses a product code.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the code is taken.</returns>
    Task<bool> CodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Issues the next code in a firm's product sequence.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="prefix">The prefix the sequence runs under.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next unused code.</returns>
    /// <remarks>
    /// The reference application issues the next number when the code field is left
    /// blank - PRO-1004 becomes PRO-1005 - and this reproduces that. It reads the
    /// highest existing suffix rather than counting rows, because products are
    /// withdrawn rather than deleted and a count would eventually reissue a code that
    /// is still in use.
    /// <para>
    /// The unique index remains the arbiter: two people saving at once can both be
    /// issued the same number, and the second insert is the one that fails.
    /// </para>
    /// </remarks>
    Task<string> NextCodeAsync(
        FirmId firmId,
        string prefix,
        CancellationToken cancellationToken = default);

    /// <summary>Loads several products at once.</summary>
    /// <param name="ids">The products.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The products that exist, by identifier.</returns>
    /// <remarks>
    /// One query rather than one per line. A stock document naming forty products
    /// would otherwise be forty round trips before it could refuse the first bad one.
    /// </remarks>
    Task<IReadOnlyDictionary<ProductId, Product>> GetManyAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a product.</summary>
    /// <param name="product">The product to add.</param>
    void Add(Product product);
}

/// <summary>Reads and writes stock documents.</summary>
public interface IStockDocumentRepository
{
    /// <summary>Finds a stock document with its lines.</summary>
    /// <param name="id">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    Task<StockDocument?> FindAsync(
        StockDocumentId id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a stock document.</summary>
    /// <param name="document">The document to add.</param>
    void Add(StockDocument document);
}

/// <summary>Reads and writes stock positions.</summary>
/// <remarks>
/// Positions are loaded for update in one query per document rather than one per
/// line. A transfer of forty products would otherwise be eighty round trips, and the
/// ordering of those round trips is what decides whether two concurrent transfers
/// deadlock.
/// </remarks>
public interface IStockBalanceRepository
{
    /// <summary>Loads the positions of several products in one warehouse.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="productIds">The products.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The positions that exist, by product. Missing means never traded.</returns>
    Task<IReadOnlyDictionary<ProductId, StockBalance>> GetPositionsAsync(
        FirmId firmId,
        WarehouseId warehouseId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>Says whether a product holds stock anywhere in the firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if any warehouse holds any of it.</returns>
    /// <remarks>
    /// Asked before batch tracking is turned on for a product. Stock that arrived
    /// before the switch belongs to no batch, and the position would then hold a
    /// quantity its batches cannot account for - a discrepancy between the stock
    /// valuation and the batch-wise one that nothing later could resolve.
    /// </remarks>
    Task<bool> HasStockAsync(
        FirmId firmId,
        ProductId productId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a position opened by its first movement.</summary>
    /// <param name="balance">The position.</param>
    void Add(StockBalance balance);
}

/// <summary>Reads and writes batches.</summary>
public interface IBatchRepository
{
    /// <summary>Finds one batch.</summary>
    /// <param name="id">The batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batch, or <see langword="null"/>.</returns>
    Task<Batch?> FindAsync(BatchId id, CancellationToken cancellationToken = default);

    /// <summary>Loads several batches at once.</summary>
    /// <param name="ids">The batches.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batches that exist, by identifier.</returns>
    Task<IReadOnlyDictionary<BatchId, Batch>> GetManyAsync(
        IReadOnlyCollection<BatchId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the batches of several products by their numbers.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="numbers">The product and batch number of each line that named one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batches that exist, by product and number.</returns>
    /// <remarks>
    /// One query for the whole document rather than one per line, and keyed by the
    /// pair because a batch number is only unique within its product: two suppliers
    /// both numbering their lots <c>001</c> is the ordinary case.
    /// </remarks>
    Task<IReadOnlyDictionary<(ProductId Product, string Number), Batch>> GetByNumbersAsync(
        FirmId firmId,
        IReadOnlyCollection<(ProductId Product, string Number)> numbers,
        CancellationToken cancellationToken = default);

    /// <summary>Reads how far the generated numbering of each product has got.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="productIds">The products.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The highest sequence issued per product; missing means none yet.</returns>
    Task<IReadOnlyDictionary<ProductId, int>> GetHighestAutoSequencesAsync(
        FirmId firmId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a batch.</summary>
    /// <param name="batch">The batch.</param>
    void Add(Batch batch);
}

/// <summary>Reads and writes the position of a batch in a warehouse.</summary>
public interface IBatchBalanceRepository
{
    /// <summary>Loads the positions of several batches in one warehouse.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="batchIds">The batches.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The positions that exist, by batch. Missing means never held here.</returns>
    Task<IReadOnlyDictionary<BatchId, BatchBalance>> GetPositionsAsync(
        FirmId firmId,
        WarehouseId warehouseId,
        IReadOnlyCollection<BatchId> batchIds,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a position opened by its first movement.</summary>
    /// <param name="balance">The position.</param>
    void Add(BatchBalance balance);
}

/// <summary>Writes and reads back the stock ledger.</summary>
public interface IStockLedgerRepository
{
    /// <summary>Records a movement.</summary>
    /// <param name="entry">The movement.</param>
    void Add(StockLedgerEntry entry);

    /// <summary>Reads the movements a document produced, oldest first.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The movements, in the order they were posted.</returns>
    /// <remarks>
    /// Cancelling a document reverses it from these rather than from its lines,
    /// because they are what actually happened: they carry the cost each movement was
    /// valued at, which a line of a transfer or an issue never held.
    /// </remarks>
    Task<IReadOnlyList<StockLedgerEntry>> ForDocumentAsync(
        StockDocumentId documentId,
        CancellationToken cancellationToken = default);
}
