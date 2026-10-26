using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Numbering;

/// <summary>Strongly typed identifier for a <see cref="MasterCodeSequence"/>.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct MasterCodeSequenceId(Guid Value) : IStronglyTypedId<MasterCodeSequenceId>
{
    /// <inheritdoc />
    public static MasterCodeSequenceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static MasterCodeSequenceId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where a master's automatic codes have got to: the last code issued to a record
/// whose code was left blank.
/// </summary>
/// <remarks>
/// <para>
/// One per master and firm. The first automatic code is 10000 and each after it is one
/// more than the last, so a list of products added without codes reads 10000, 10001,
/// 10002. A code somebody typed takes no part: it is kept as typed, and the sequence
/// carries on from where it was.
/// </para>
/// <para>
/// A typed code can still land on a number the sequence has not reached - somebody
/// keys 10005 by hand - so the next code is the first number past the last one issued
/// that no record holds. That is checked by whoever issues it, against the master's own
/// codes; this only remembers how far it has got.
/// </para>
/// </remarks>
public sealed class MasterCodeSequence : AggregateRoot<MasterCodeSequenceId>, IFirmScoped, IAuditable
{
    /// <summary>The first code a master's sequence issues.</summary>
    public const long FirstCode = 10000;

    /// <summary>The longest name a master is known by here.</summary>
    public const int MaximumMasterLength = 40;

    private MasterCodeSequence(MasterCodeSequenceId id, TenantId tenantId, FirmId firmId, string master)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Master = master;
    }

    private MasterCodeSequence()
    {
        Master = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the master the sequence numbers: product, customer, category and so on.</summary>
    public string Master { get; private set; }

    /// <summary>Gets the last code issued; zero before the first.</summary>
    public long LastIssued { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets the first number the next code may be: one past the last, and never below 10000.</summary>
    public long Next => Math.Max(LastIssued + 1, FirstCode);

    /// <summary>Starts a master's sequence, which issues 10000 first.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="master">The master.</param>
    /// <returns>The sequence.</returns>
    public static MasterCodeSequence Start(TenantId tenantId, FirmId firmId, string master)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(master);

        return new MasterCodeSequence(MasterCodeSequenceId.NewId(), tenantId, firmId, master.Trim());
    }

    /// <summary>Records a code as issued, so the next starts after it.</summary>
    /// <param name="code">The code issued; never behind the sequence.</param>
    public void Issue(long code)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(code, Next);

        LastIssued = code;
    }
}
