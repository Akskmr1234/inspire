using ERP.Application.Accounting.Reports;
using ERP.Application.Purchase;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
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

    /// <summary>Lists the ledgers of one kind, optionally narrowed by a search term.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="kind">What the ledgers represent.</param>
    /// <param name="search">Matched against code, name and mobile number.</param>
    /// <param name="activeOnly">Whether to exclude withdrawn ledgers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ledgers, ordered by name.</returns>
    /// <remarks>
    /// The mobile number is searched alongside the code and the name because that is
    /// how a counter finds a customer: section 12.1's lookup is by the number somebody
    /// reads off a phone, not by an account code nobody at a till has ever seen.
    /// </remarks>
    Task<IReadOnlyList<Ledger>> ListByKindAsync(
        FirmId firmId,
        LedgerKind kind,
        string? search = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an account group.</summary>
    /// <param name="id">The group.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The group, or <see langword="null"/>.</returns>
    Task<AccountGroup?> FindGroupAsync(
        AccountGroupId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an account group by its code within a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The group code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The group, or <see langword="null"/>.</returns>
    Task<AccountGroup?> FindGroupByCodeAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Says whether a ledger code is already used in a firm.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the code is taken.</returns>
    Task<bool> IsCodeInUseAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a ledger.</summary>
    /// <param name="ledger">The ledger.</param>
    void Add(Ledger ledger);
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

/// <summary>Reads the accounts a firm's stock movements post to.</summary>
public interface IInventoryAccountMapRepository
{
    /// <summary>Finds a firm's map.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The map, or <see langword="null"/> where the firm has none.</returns>
    Task<InventoryAccountMap?> FindAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a map for a firm that has none.</summary>
    /// <param name="map">The map.</param>
    void Add(InventoryAccountMap map);
}

/// <summary>Reads the figures a statutory tax return is built from.</summary>
/// <remarks>
/// The two halves come from different places on purpose, which is the business's answer of
/// 2026-08-12: output tax from the sales documents, because a return states supplies by
/// rate and only a document line knows the rate and the value it was charged on; input tax
/// from postings to the accounts the firm's tax map names, because nothing else produces
/// it yet and purchase will land in the same accounts when it arrives.
/// </remarks>
public interface ITaxReturnReader
{
    /// <summary>Reads the output tax charged over a period.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    Task<OutputTaxReport> ReadOutputAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the input tax incurred over a period.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    Task<InputTaxReport> ReadInputAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what the firm owes the state for a period.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    Task<TaxSummaryReport> ReadSummaryAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>What a sales list is narrowed by.</summary>
/// <param name="From">The earliest document date, or no lower bound.</param>
/// <param name="To">The latest, or no upper bound.</param>
/// <param name="Kind">Invoices or returns, or both.</param>
/// <param name="Status">One lifecycle state, or all.</param>
/// <param name="CustomerLedgerId">One customer, or all.</param>
/// <param name="Search">Matched against the document number and the customer's reference.</param>
public sealed record SalesInvoiceFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    SalesDocumentKind? Kind = null,
    SalesInvoiceStatus? Status = null,
    LedgerId? CustomerLedgerId = null,
    string? Search = null);

/// <summary>Reads sales documents for a list.</summary>
/// <remarks>
/// Its own reader rather than the repository, because listing and posting want different
/// things: a posting loads one document whole, and a list wants a page of many with their
/// customers' names attached and nothing else.
/// </remarks>
public interface ISalesInvoiceReader
{
    /// <summary>Reads one page of the documents a filter matches, newest first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="filter">What to narrow by.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page, and how many rows the filter matched in total.</returns>
    Task<PagedResult<SalesInvoiceSummary>> ListAsync(
        FirmId firmId,
        SalesInvoiceFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>What a purchase list is narrowed by.</summary>
/// <param name="From">The earliest document date, or no lower bound.</param>
/// <param name="To">The latest, or no upper bound.</param>
/// <param name="Kind">Purchases or returns, or both.</param>
/// <param name="Status">One lifecycle state, or all.</param>
/// <param name="SupplierLedgerId">One supplier, or all.</param>
/// <param name="Search">
/// Matched against the firm's own number and the supplier's invoice number. Both, because
/// somebody looking a purchase up is as likely to be holding the supplier's document as
/// the firm's own entry.
/// </param>
public sealed record PurchaseInvoiceFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    PurchaseDocumentKind? Kind = null,
    PurchaseInvoiceStatus? Status = null,
    LedgerId? SupplierLedgerId = null,
    string? Search = null);

/// <summary>Reads purchase documents for a list.</summary>
/// <remarks>
/// Its own reader rather than the repository, for the reason the sales one has its own:
/// posting loads one document whole, and a list wants a page of many with their suppliers'
/// names attached and nothing else.
/// </remarks>
public interface IPurchaseInvoiceReader
{
    /// <summary>Reads one page of the documents a filter matches, newest first.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="filter">What to narrow by.</param>
    /// <param name="page">Which page, from one.</param>
    /// <param name="pageSize">How many rows a page holds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page, and how many rows the filter matched in total.</returns>
    Task<PagedResult<PurchaseInvoiceSummary>> ListAsync(
        FirmId firmId,
        PurchaseInvoiceFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads the charges a firm's documents may carry.</summary>
public interface IAdditionalLedgerRepository
{
    /// <summary>Lists the charges mapped to one kind of document.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="document">The kind of document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The charges, in the order the firm arranged them.</returns>
    Task<IReadOnlyList<AdditionalLedger>> ListForDocumentAsync(
        FirmId firmId,
        ChargeableDocument document,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes sales invoices.</summary>
public interface ISalesInvoiceRepository
{
    /// <summary>Finds an invoice with everything posting it will need.</summary>
    /// <param name="id">The invoice.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The invoice, or <see langword="null"/> where there is none.</returns>
    /// <remarks>
    /// Lines, charges, per-head tax and the units each line sells, all in one load.
    /// Posting reads every one of them - the lines become a stock issue, the charges and
    /// the tax become journal lines - and fetching them lazily would be four round trips
    /// inside a transaction holding stock positions.
    /// </remarks>
    Task<SalesInvoice?> FindAsync(
        SalesInvoiceId id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an invoice.</summary>
    /// <param name="invoice">The invoice.</param>
    void Add(SalesInvoice invoice);
}

/// <summary>Reads and writes purchase invoices.</summary>
public interface IPurchaseInvoiceRepository
{
    /// <summary>Finds a purchase with everything posting it will need.</summary>
    /// <param name="id">The purchase.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The purchase, or <see langword="null"/> where there is none.</returns>
    /// <remarks>
    /// Lines, charges, per-head tax and the units each line brings in, all in one load,
    /// for the reason a sale's are: posting reads every one of them, and fetching them
    /// lazily would be four round trips inside a transaction holding stock positions.
    /// </remarks>
    Task<PurchaseInvoice?> FindAsync(
        PurchaseInvoiceId id,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a purchase.</summary>
    /// <param name="invoice">The purchase.</param>
    void Add(PurchaseInvoice invoice);

    /// <summary>Asks whether a supplier's own invoice number has already been entered.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="supplierLedgerId">The supplier.</param>
    /// <param name="supplierInvoiceNumber">The number printed on their invoice.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> where that supplier's invoice is already on file.</returns>
    /// <remarks>
    /// Asked before the document is built, so the commonest mistake in a purchase ledger -
    /// keying the same supplier invoice twice - is reported as itself. The unique index
    /// behind it stays as the backstop, but an index violation reaches an operator as a
    /// message about an index.
    /// </remarks>
    Task<bool> IsSupplierInvoiceNumberInUseAsync(
        FirmId firmId,
        LedgerId supplierLedgerId,
        string supplierInvoiceNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads the accounts a firm's tax heads post to.</summary>
public interface ITaxAccountMapRepository
{
    /// <summary>Finds a firm's map.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The map, or <see langword="null"/> where the firm has none.</returns>
    Task<TaxAccountMap?> FindAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a map for a firm that has none.</summary>
    /// <param name="map">The map.</param>
    void Add(TaxAccountMap map);
}

/// <summary>Reads and writes serialised units.</summary>
public interface ISerialNumberRepository
{
    /// <summary>Loads several units at once.</summary>
    /// <param name="ids">The units.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units that exist, by identifier.</returns>
    Task<IReadOnlyDictionary<SerialNumberId, SerialNumber>> GetManyAsync(
        IReadOnlyCollection<SerialNumberId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the units of several products by their numbers.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="numbers">The product and number of each unit a document names.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units that exist, by product and number.</returns>
    /// <remarks>
    /// Keyed by the pair, because a serial number is unique only within its product:
    /// two manufacturers numbering their units from 0001 is the ordinary case, and a
    /// firm selling both would otherwise have one of them shadow the other.
    /// </remarks>
    Task<IReadOnlyDictionary<(ProductId Product, string Number), SerialNumber>>
        GetByNumbersAsync(
            FirmId firmId,
            IReadOnlyCollection<(ProductId Product, string Number)> numbers,
            CancellationToken cancellationToken = default);

    /// <summary>Loads the units a document's lines name.</summary>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The units, by identifier.</returns>
    /// <remarks>
    /// Read from the lines rather than from the units, because a unit records only the
    /// document that moved it last: cancelling one entered six months ago has to find
    /// the units <em>it</em> moved, not the ones that have moved since.
    /// </remarks>
    Task<IReadOnlyDictionary<SerialNumberId, SerialNumber>> ForDocumentAsync(
        StockDocumentId documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a unit.</summary>
    /// <param name="serial">The unit.</param>
    void Add(SerialNumber serial);
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
