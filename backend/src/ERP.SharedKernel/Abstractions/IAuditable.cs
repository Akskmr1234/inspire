using ERP.SharedKernel.Tenancy;

namespace ERP.SharedKernel.Abstractions;

/// <summary>
/// Records who created and last changed a row, and when. Populated by an EF Core
/// interceptor rather than by application code, so it cannot be forgotten.
/// </summary>
/// <remarks>
/// This is the lightweight "stamp" audit that lives on the row itself. It
/// answers "who touched this, and when". The separate audit-trail table answers
/// "what exactly changed", recording old and new values per property as the
/// specification requires. Both exist because the first is cheap and always
/// available for display, while the second is expensive and consulted rarely.
/// </remarks>
public interface IAuditable
{
    /// <summary>Gets the instant the row was created, in UTC.</summary>
    DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the user who created the row.</summary>
    UserId CreatedBy { get; }

    /// <summary>Gets the instant of the most recent change, in UTC, if any.</summary>
    DateTimeOffset? ModifiedAtUtc { get; }

    /// <summary>Gets the user who last changed the row, if any.</summary>
    UserId? ModifiedBy { get; }
}

/// <summary>
/// Marks an entity as never physically deleted, only flagged as removed.
/// </summary>
/// <remarks>
/// Accounting records must not vanish. A deleted voucher still has to be
/// reachable for an audit, and a deleted ledger may still be referenced by
/// historical postings whose totals must continue to reconcile. A hard
/// <c>DELETE</c> would either break referential integrity or silently rewrite
/// history; neither is acceptable in a system of financial record.
/// A global query filter hides flagged rows from normal queries.
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>Gets a value indicating whether the row has been deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>Gets the instant of deletion, in UTC, if deleted.</summary>
    DateTimeOffset? DeletedAtUtc { get; }

    /// <summary>Gets the user who deleted the row, if deleted.</summary>
    UserId? DeletedBy { get; }
}
