using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Accounting;

/// <summary>Which way a tax head runs on a document.</summary>
/// <remarks>
/// The same head has two accounts. VAT a firm charges its customers is a liability owed
/// to the state; VAT it pays its suppliers is an asset recoverable from them, and a
/// return is the difference. One account for both would net them silently and leave
/// nothing to file.
/// </remarks>
public enum TaxDirection
{
    /// <summary>Tax charged to a customer. A liability.</summary>
    Output = 1,

    /// <summary>Tax paid to a supplier. Recoverable, so an asset.</summary>
    Input = 2,
}

/// <summary>
/// The accounts one firm's tax heads post to.
/// </summary>
/// <remarks>
/// <para>
/// The business's answer of 2026-08-10, and the thing that makes one posting produce a
/// correct return in either jurisdiction: a VAT firm credits Output VAT, a GST firm
/// credits CGST, SGST or IGST as the engine assessed them, plus cess. The invoice does
/// not choose - the engine says which heads applied, and this says where each one lands.
/// </para>
/// <para>
/// Reading ledgers by their seeded codes was offered and declined, for the reason that
/// settles it: a firm that renames or recodes an account would silently break its own
/// tax postings, and would find out at a filing deadline.
/// </para>
/// <para>
/// Only the heads a firm's regime actually uses need an account. A VAT firm has no CGST
/// to post and asking it to choose one would be asking about a tax it does not pay.
/// </para>
/// </remarks>
public sealed class TaxAccountMap : AggregateRoot<TaxAccountMapId>, IFirmScoped, IAuditable
{
    private readonly List<TaxAccountAssignment> _accounts = [];

    private TaxAccountMap(TaxAccountMapId id, TenantId tenantId, FirmId firmId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private TaxAccountMap()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the account each head posts to, in each direction.</summary>
    public IReadOnlyList<TaxAccountAssignment> Accounts => _accounts.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Starts an empty map for a firm.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <returns>The map, with nothing chosen yet.</returns>
    public static TaxAccountMap Create(TenantId tenantId, FirmId firmId) =>
        new(TaxAccountMapId.NewId(), tenantId, firmId);

    /// <summary>Chooses the account one head posts to in one direction.</summary>
    /// <param name="component">The tax head.</param>
    /// <param name="direction">Output or input.</param>
    /// <param name="ledger">The account it posts to.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Assign(TaxComponentType component, TaxDirection direction, Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (!Enum.IsDefined(component) || !Enum.IsDefined(direction))
        {
            return Result.Failure(Error.Validation(
                "TaxAccounts.UnknownHead",
                $"'{component}' {direction} is not a tax head this system knows."));
        }

        if (ledger.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "TaxAccounts.LedgerNotInFirm", $"'{ledger.Name}' belongs to another firm."));
        }

        if (!ledger.IsActive)
        {
            return Result.Failure(Error.BusinessRule(
                "TaxAccounts.LedgerWithdrawn",
                $"'{ledger.Name}' has been withdrawn from use and cannot be posted to."));
        }

        TaxAccountAssignment? existing = _accounts.Find(entry =>
            entry.Component == component && entry.Direction == direction);

        if (existing is null)
        {
            _accounts.Add(
                new TaxAccountAssignment(TenantId, Id, component, direction, ledger.Id));
        }
        else
        {
            existing.PointAt(ledger.Id);
        }

        return Result.Success();
    }

    /// <summary>Reads the account a head posts to.</summary>
    /// <param name="component">The tax head.</param>
    /// <param name="direction">Output or input.</param>
    /// <returns>The account, or the reason there is none.</returns>
    /// <remarks>
    /// A failure here stops an invoice posting, and it names the head that is missing.
    /// A firm that has started charging a tax it has no account for is a firm whose
    /// return will not reconcile, and stopping at the first invoice is the cheapest
    /// moment for that to be noticed.
    /// </remarks>
    public Result<LedgerId> For(TaxComponentType component, TaxDirection direction) =>
        _accounts.Find(entry => entry.Component == component && entry.Direction == direction)
            is { } assigned
            ? Result.Success(assigned.LedgerId)
            : Result.Failure<LedgerId>(Error.BusinessRule(
                "TaxAccounts.NotConfigured",
                $"This firm has not chosen which account {direction} {component} posts to. "
                + "Set the tax accounts up before posting a document that charges it."));
}

/// <summary>One tax head in one direction, and the account it posts to.</summary>
public sealed class TaxAccountAssignment : ITenantScoped
{
    internal TaxAccountAssignment(
        TenantId tenantId,
        TaxAccountMapId mapId,
        TaxComponentType component,
        TaxDirection direction,
        LedgerId ledgerId)
    {
        TenantId = tenantId;
        TaxAccountMapId = mapId;
        Component = component;
        Direction = direction;
        LedgerId = ledgerId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private TaxAccountAssignment()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the map this choice belongs to.</summary>
    public TaxAccountMapId TaxAccountMapId { get; private set; }

    /// <summary>Gets the tax head.</summary>
    public TaxComponentType Component { get; private set; }

    /// <summary>Gets whether the head is charged out or paid in.</summary>
    public TaxDirection Direction { get; private set; }

    /// <summary>Gets the account it posts to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Points this head at another account.</summary>
    /// <param name="ledgerId">The account.</param>
    /// <remarks>
    /// Affects what happens next, never what has happened: postings already filed name
    /// the account they were made to, and a return already submitted was built from
    /// those postings rather than from this map.
    /// </remarks>
    internal void PointAt(LedgerId ledgerId) => LedgerId = ledgerId;
}
