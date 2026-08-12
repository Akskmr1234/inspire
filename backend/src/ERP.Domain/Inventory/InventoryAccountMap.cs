using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>Which account the other side of a stock movement lands in.</summary>
/// <remarks>
/// The counter-accounts named when open question 8a was answered. Inventory itself is
/// one account per firm; what differs between movements is where the value comes from
/// or goes to, and that difference is what an accountant reads the trial balance for -
/// goods consumed by production are not goods dropped on a floor.
/// </remarks>
public enum StockAccount
{
    /// <summary>The asset the value of stock sits in. Debited in, credited out.</summary>
    Inventory = 1,

    /// <summary>Where goods issued to be used up go. A material issue's other side.</summary>
    Consumption = 2,

    /// <summary>Where goods written off go: damaged stock, and a shortfall on a count.</summary>
    Loss = 3,

    /// <summary>What an opening-stock document credits: the value the firm started with.</summary>
    OpeningEquity = 4,

    /// <summary>Where a correction goes: an adjustment, and a surplus found on a count.</summary>
    Variance = 5,

    /// <summary>What goods sold cost. The other side of a sale's stock issue.</summary>
    /// <remarks>
    /// Its own account rather than sharing Consumption, which is the business's answer of
    /// 2026-08-10. Revenue less cost of goods sold is the gross margin; mixing factory
    /// consumption into it produces a figure that answers no question anybody asked.
    /// </remarks>
    CostOfGoodsSold = 6,

    /// <summary>Where a sale's revenue is credited.</summary>
    /// <remarks>
    /// One account per firm, which is the business's answer of 2026-08-10. Per category
    /// and per document type were both offered and declined, so revenue by line of
    /// business is a report over the invoice lines rather than a split in the chart.
    /// </remarks>
    SalesRevenue = 7,

    /// <summary>Where the difference goes when a document total is rounded.</summary>
    /// <remarks>
    /// Named by the answer to the rounding question of 2026-08-10: tax is computed per
    /// component at full precision, only the total is rounded, and the difference is
    /// posted rather than hidden inside the total.
    /// <para>
    /// On the map rather than found by its code, for the reason that decided the tax
    /// accounts: a firm that renames or recodes its Round Off ledger would otherwise
    /// break its own postings silently. Section 9 still lists Round Off among the
    /// additional ledgers, and it stays there for the charge somebody adds by hand -
    /// this is where the difference the system computes lands, which is not the same
    /// question.
    /// </para>
    /// </remarks>
    RoundOff = 8,
}

/// <summary>
/// The accounts one firm's stock movements post to.
/// </summary>
/// <remarks>
/// <para>
/// The answer to open question 8a, made data. Which control account inventory sits in,
/// and which account each kind of movement takes its other side from, are a firm's own
/// choice of chart - so they are chosen once per firm and read on every posting rather
/// than guessed at per document.
/// </para>
/// <para>
/// Per firm rather than per product category. The finer grain was offered and declined,
/// and nothing here forecloses it: this is a lookup taking a movement, and a category
/// can be added to what it looks up without changing a single posting.
/// </para>
/// <para>
/// A firm whose map is incomplete cannot post stock. That is deliberate and it is the
/// whole point of having asked the question: a movement posted into an account nobody
/// chose is worse than a movement refused, because the first is found at a
/// reconciliation months later and the second is found immediately by the person who
/// can fix it.
/// </para>
/// </remarks>
public sealed class InventoryAccountMap : AggregateRoot<InventoryAccountMapId>, IFirmScoped, IAuditable
{
    private readonly List<InventoryAccountAssignment> _accounts = [];

    private InventoryAccountMap(InventoryAccountMapId id, TenantId tenantId, FirmId firmId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private InventoryAccountMap()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the account each kind of movement posts to.</summary>
    public IReadOnlyList<InventoryAccountAssignment> Accounts => _accounts.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether every account a posting could need has been chosen.</summary>
    public bool IsComplete =>
        Enum.GetValues<StockAccount>()
            .All(account => _accounts.Exists(entry => entry.Account == account));

    /// <summary>Starts an empty map for a firm.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <returns>The map, with nothing chosen yet.</returns>
    public static InventoryAccountMap Create(TenantId tenantId, FirmId firmId) =>
        new(InventoryAccountMapId.NewId(), tenantId, firmId);

    /// <summary>Chooses the account one kind of movement posts to.</summary>
    /// <param name="account">The kind.</param>
    /// <param name="ledger">The account it posts to.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The ledger has to belong to the same firm, which is the one thing this can check
    /// and the one that would otherwise post a firm's stock into another firm's books.
    /// What it does not check is whether the account is of a sensible nature - an
    /// inventory control account that is not an asset is a strange choice, but it is a
    /// choice, and a chart this system did not design is not one it should overrule.
    /// </remarks>
    public Result Assign(StockAccount account, Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (!Enum.IsDefined(account))
        {
            return Result.Failure(Error.Validation(
                "InventoryAccounts.UnknownAccount",
                $"'{account}' is not a kind of stock posting."));
        }

        if (ledger.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "InventoryAccounts.LedgerNotInFirm",
                $"'{ledger.Name}' belongs to another firm."));
        }

        if (!ledger.IsActive)
        {
            return Result.Failure(Error.BusinessRule(
                "InventoryAccounts.LedgerWithdrawn",
                $"'{ledger.Name}' has been withdrawn from use and cannot be posted to."));
        }

        InventoryAccountAssignment? existing =
            _accounts.Find(entry => entry.Account == account);

        if (existing is null)
        {
            _accounts.Add(new InventoryAccountAssignment(TenantId, Id, account, ledger.Id));
        }
        else
        {
            existing.PointAt(ledger.Id);
        }

        return Result.Success();
    }

    /// <summary>Reads the account a movement posts to.</summary>
    /// <param name="account">The kind of movement.</param>
    /// <returns>The account, or the reason there is none.</returns>
    /// <remarks>
    /// A failure here stops a stock document posting, and the message names the account
    /// that is missing rather than saying the map is incomplete: the person reading it
    /// has to go and choose one thing, and telling them which one is the difference
    /// between a minute's work and a hunt through a settings screen.
    /// </remarks>
    public Result<LedgerId> For(StockAccount account) =>
        _accounts.Find(entry => entry.Account == account) is { } assigned
            ? Result.Success(assigned.LedgerId)
            : Result.Failure<LedgerId>(Error.BusinessRule(
                "InventoryAccounts.NotConfigured",
                $"This firm has not chosen which account {Describe(account)}. Set the "
                + "inventory accounts up before posting stock."));

    private static string Describe(StockAccount account) => account switch
    {
        StockAccount.Inventory => "stock is held in",
        StockAccount.Consumption => "issued goods are consumed to",
        StockAccount.Loss => "written-off goods are charged to",
        StockAccount.OpeningEquity => "opening stock is credited to",
        StockAccount.Variance => "stock corrections are posted to",
        StockAccount.CostOfGoodsSold => "the cost of goods sold is charged to",
        StockAccount.SalesRevenue => "sales are credited to",
        StockAccount.RoundOff => "rounding differences are posted to",
        _ => account.ToString(),
    };
}

/// <summary>One kind of stock posting, and the account it lands in.</summary>
/// <remarks>
/// A row rather than a column on the map. The kinds of posting are data - the answer to
/// open question 8a left room for more of them - and a sixth kind should be a row
/// somebody adds, not a migration that widens every firm's map.
/// </remarks>
public sealed class InventoryAccountAssignment : ITenantScoped
{
    internal InventoryAccountAssignment(
        TenantId tenantId,
        InventoryAccountMapId mapId,
        StockAccount account,
        LedgerId ledgerId)
    {
        TenantId = tenantId;
        InventoryAccountMapId = mapId;
        Account = account;
        LedgerId = ledgerId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private InventoryAccountAssignment()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the map this choice belongs to.</summary>
    public InventoryAccountMapId InventoryAccountMapId { get; private set; }

    /// <summary>Gets the kind of posting.</summary>
    public StockAccount Account { get; private set; }

    /// <summary>Gets the account it lands in.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Points this kind of posting at another account.</summary>
    /// <param name="ledgerId">The account.</param>
    /// <remarks>
    /// Changing where a kind of movement posts affects what happens next, never what
    /// has already happened: postings already in the books name the account they were
    /// made to, and nothing here reaches back to them.
    /// </remarks>
    internal void PointAt(LedgerId ledgerId) => LedgerId = ledgerId;
}
