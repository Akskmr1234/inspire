using System.Diagnostics.CodeAnalysis;

namespace ERP.SharedKernel.Primitives;

/// <summary>
/// The entry point to an aggregate: the one entity outside code may hold a
/// reference to, and the boundary within which invariants are guaranteed
/// consistent.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// <para>
/// Aggregate boundaries are the load-bearing design decision in this system.
/// A <c>Voucher</c> together with its lines is one aggregate, because
/// "debits equal credits" must hold at every commit and can only be enforced
/// if they are saved together. A <c>Ledger</c> is a separate aggregate
/// referenced by identifier, because a voucher must not be able to alter a
/// ledger as a side effect of being posted.
/// </para>
/// <para>
/// Rule of thumb for this codebase: one aggregate per transaction. If two
/// aggregates must change together, the second changes in a domain-event
/// handler, not in the same method.
/// </para>
/// </remarks>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initialises a new instance of the <see cref="AggregateRoot{TId}"/> class.</summary>
    /// <param name="id">The aggregate identifier.</param>
    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="AggregateRoot{TId}"/> class
    /// for EF Core materialisation.
    /// </summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Gets the events raised since this aggregate was loaded, awaiting dispatch
    /// after the transaction commits.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Gets the optimistic-concurrency token, mapped to the PostgreSQL system
    /// column <c>xmin</c>.
    /// </summary>
    /// <remarks>
    /// Two users editing the same document is routine in an ERP. Without this,
    /// the second save silently overwrites the first. With it, EF Core raises
    /// <c>DbUpdateConcurrencyException</c> and the API can return HTTP 409 so
    /// the user is told to reload rather than losing their colleague's work.
    /// Using <c>xmin</c> means PostgreSQL maintains the token itself; there is
    /// no application-managed version column to forget to increment.
    /// </remarks>
    [SuppressMessage(
        "Major Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification =
            "The setter is invoked by EF Core, which materialises the xmin " +
            "concurrency token through the property. No application code writes " +
            "it - deliberately, since PostgreSQL owns the value - so the " +
            "analyzer cannot see the caller.")]
    public uint Version { get; private set; }

    /// <summary>Clears the recorded events once they have been dispatched.</summary>
    /// <remarks>Called by the infrastructure layer after publication, not by domain code.</remarks>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Records an event to be published after the transaction commits.</summary>
    /// <param name="domainEvent">The event that occurred.</param>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
