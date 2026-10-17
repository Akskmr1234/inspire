using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>
/// Which record a row of another system became when it was imported.
/// </summary>
/// <remarks>
/// <para>
/// An import is run more than once in practice: a first attempt stops half way, a
/// firm brings its products in on Monday and its balances on Friday, a mistake in the
/// old system is corrected and the export taken again. Each time, what was already
/// brought in has to be recognised rather than created a second time, and the old
/// system's own identifiers are the only reliable way to recognise it - names change and
/// codes collide.
/// </para>
/// <para>
/// A link records the old identifier against the new one and nothing else. It is never
/// read by anything but the import, and deleting the record it points at leaves it
/// harmlessly pointing at nothing until the next import replaces it.
/// </para>
/// </remarks>
public sealed class ImportLink : IFirmScoped
{
    /// <summary>The longest source, kind or key accepted.</summary>
    public const int MaximumKeyLength = 100;

    private ImportLink(
        TenantId tenantId,
        FirmId firmId,
        string source,
        string kind,
        string legacyKey,
        Guid targetId,
        DateTimeOffset linkedAtUtc)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Source = source;
        Kind = kind;
        LegacyKey = legacyKey;
        TargetId = targetId;
        LinkedAtUtc = linkedAtUtc;
    }

    private ImportLink()
    {
        Source = string.Empty;
        Kind = string.Empty;
        LegacyKey = string.Empty;
    }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the firm the record was imported into.</summary>
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the system it came from, such as <c>easyretail</c>.</summary>
    public string Source { get; private set; }

    /// <summary>Gets what kind of record it is there, such as <c>ledger</c> or <c>product</c>.</summary>
    public string Kind { get; private set; }

    /// <summary>Gets its identifier in the system it came from.</summary>
    public string LegacyKey { get; private set; }

    /// <summary>Gets the identifier of the record it became.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>Gets when it was brought in.</summary>
    public DateTimeOffset LinkedAtUtc { get; private set; }

    /// <summary>Records that a row of another system became a record here.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The firm it was imported into.</param>
    /// <param name="source">The system it came from.</param>
    /// <param name="kind">What kind of record it is there.</param>
    /// <param name="legacyKey">Its identifier there.</param>
    /// <param name="targetId">The record it became.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The link.</returns>
    public static ImportLink Record(
        TenantId tenantId,
        FirmId firmId,
        string source,
        string kind,
        string legacyKey,
        Guid targetId,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyKey);

        return new ImportLink(tenantId, firmId, source, kind, legacyKey, targetId, nowUtc);
    }

    /// <summary>Points the link at another record, when the one it named has gone.</summary>
    /// <param name="targetId">The record it now became.</param>
    /// <param name="nowUtc">The current instant.</param>
    public void Relink(Guid targetId, DateTimeOffset nowUtc)
    {
        TargetId = targetId;
        LinkedAtUtc = nowUtc;
    }
}
